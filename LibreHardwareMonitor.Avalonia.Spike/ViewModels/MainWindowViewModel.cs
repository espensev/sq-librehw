using LibreHardwareMonitor.Avalonia.Spike.Core.Contracts;

namespace LibreHardwareMonitor.Avalonia.Spike.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private static readonly IReadOnlyList<FixtureChoice> _fixtureChoices =
        Array.AsReadOnly(
        [
            new FixtureChoice("normal.json", Path.Combine("Fixtures", "normal.json")),
            new FixtureChoice("unavailable.json", Path.Combine("Fixtures", "unavailable.json")),
            new FixtureChoice("hotplug-before.json", Path.Combine("Fixtures", "hotplug-before.json")),
            new FixtureChoice("hotplug-after.json", Path.Combine("Fixtures", "hotplug-after.json")),
            new FixtureChoice("malformed.json", Path.Combine("Fixtures", "malformed.json")),
            new FixtureChoice("oversized-string.json", Path.Combine("Fixtures", "oversized-string.json")),
        ]);

    private readonly object _requestGate = new();
    private readonly ISensorFixtureLoader _loader;
    private readonly SensorLoadLimits _limits;
    private CancellationTokenSource? _activeRequest;
    private long _activeRequestId;
    private IReadOnlyList<SensorNodeViewModel> _roots = Array.Empty<SensorNodeViewModel>();
    private FixtureChoice _selectedFixtureChoice;
    private string _sourceName = "None";
    private string _version = "Unavailable";
    private string _rejectionMessage = string.Empty;
    private int _totalNodeCount;
    private int _sensorCount;
    private bool _isLoading;
    private bool _hasSnapshot;
    private bool _hasError;

    public MainWindowViewModel(
        ISensorFixtureLoader loader,
        SensorLoadLimits limits)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
        _selectedFixtureChoice = FixtureChoices[0];
    }

    public IReadOnlyList<FixtureChoice> FixtureChoices => _fixtureChoices;

    public FixtureChoice SelectedFixtureChoice
    {
        get => _selectedFixtureChoice;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (!FixtureChoices.Contains(value))
            {
                throw new ArgumentException(
                    "The selected fixture must be one of the bundled choices.",
                    nameof(value));
            }

            SetProperty(ref _selectedFixtureChoice, value);
        }
    }

    public IReadOnlyList<SensorNodeViewModel> Roots => _roots;

    public string SourceName => _sourceName;

    public string Version => _version;

    public int TotalNodeCount => _totalNodeCount;

    public int SensorCount => _sensorCount;

    public bool IsLoading => _isLoading;

    public bool HasSnapshot => _hasSnapshot;

    public bool HasError => _hasError;

    public string RejectionMessage => _rejectionMessage;

    public bool ShowInitialEmptyState => !IsLoading && !HasSnapshot && !HasError;

    public bool ShowInitialErrorState => !IsLoading && !HasSnapshot && HasError;

    public bool HasRetainedSnapshotAfterError => HasSnapshot && HasError;

    public string SourceStatus => $"Source: {SourceName}";

    public string VersionStatus => $"Version: {Version}";

    public string NodeStatus => $"Nodes: {TotalNodeCount}";

    public string SensorStatus => $"Sensors: {SensorCount}";

    public string LoadStateStatus
    {
        get
        {
            if (IsLoading)
            {
                return HasSnapshot
                    ? "Loading another fixture; showing the last accepted hierarchy."
                    : "Loading fixture.";
            }

            if (HasError)
            {
                return HasSnapshot
                    ? "Fixture rejected; showing the last accepted hierarchy."
                    : "Fixture rejected; no hierarchy is loaded.";
            }

            return HasSnapshot ? "Fixture loaded." : "No fixture loaded.";
        }
    }

    public Task LoadBundledFixtureAsync()
    {
        FixtureChoice choice = SelectedFixtureChoice;
        string fixturePath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, choice.RelativePath));
        return LoadPathAsync(fixturePath);
    }

    public Task LoadLocalPathAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return LoadPathAsync(filePath);
    }

    private async Task LoadPathAsync(string filePath)
    {
        LoadRequest request = BeginRequest();

        try
        {
            SensorLoadResult result = await _loader.LoadFileAsync(
                filePath,
                _limits,
                request.Cancellation.Token);

            if (result.IsSuccess)
            {
                PublishSuccess(request, result.Snapshot!);
            }
            else
            {
                PublishFailure(request, result.Error!);
            }
        }
        catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested)
        {
            // A newer request owns publication. The request-id check is still the
            // authority when a loader ignores or races cancellation.
        }
        catch (OperationCanceledException)
        {
            PublishFailure(
                request,
                new SensorLoadError(
                    SensorLoadErrorCode.SupersededOrCancelled,
                    "The fixture load was cancelled before it completed."));
        }
        catch (Exception exception)
        {
            PublishFailure(
                request,
                new SensorLoadError(
                    SensorLoadErrorCode.IoFailure,
                    $"The fixture could not be loaded: {exception.Message}"));
        }
        finally
        {
            request.Cancellation.Dispose();
        }
    }

    private LoadRequest BeginRequest()
    {
        CancellationTokenSource cancellation = new();
        CancellationTokenSource? superseded;
        long requestId;

        lock (_requestGate)
        {
            superseded = _activeRequest;
            _activeRequest = cancellation;
            requestId = ++_activeRequestId;
            _isLoading = true;
            _hasError = false;
            _rejectionMessage = string.Empty;
        }

        try
        {
            superseded?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The superseded request completed between the ownership swap and
            // cancellation. Its request ID can no longer publish stale state.
        }

        RaiseStatePropertiesChanged();
        return new LoadRequest(requestId, cancellation);
    }

    private void PublishSuccess(LoadRequest request, SensorSnapshot snapshot)
    {
        SensorNodeViewModel[] roots = snapshot.RootNodes
            .Select(node => new SensorNodeViewModel(node))
            .ToArray();

        bool published;

        lock (_requestGate)
        {
            published = IsCurrentRequest(request);

            if (published)
            {
                _roots = Array.AsReadOnly(roots);
                _sourceName = snapshot.SourceName;
                _version = string.IsNullOrWhiteSpace(snapshot.Version)
                    ? "Unavailable"
                    : snapshot.Version;
                _totalNodeCount = snapshot.TotalNodeCount;
                _sensorCount = snapshot.SensorCount;
                _isLoading = false;
                _hasSnapshot = true;
                _hasError = false;
                _rejectionMessage = string.Empty;
                _activeRequest = null;
            }
        }

        if (published)
        {
            RaiseStatePropertiesChanged();
        }
    }

    private void PublishFailure(LoadRequest request, SensorLoadError error)
    {
        bool published;

        lock (_requestGate)
        {
            published = IsCurrentRequest(request);

            if (published)
            {
                _isLoading = false;
                _hasError = true;
                _rejectionMessage = $"{error.Code}: {error.Message}";
                _activeRequest = null;
            }
        }

        if (published)
        {
            RaiseStatePropertiesChanged();
        }
    }

    private bool IsCurrentRequest(LoadRequest request)
    {
        return request.Id == _activeRequestId &&
            ReferenceEquals(request.Cancellation, _activeRequest);
    }

    private void RaiseStatePropertiesChanged()
    {
        RaisePropertyChanged(nameof(Roots));
        RaisePropertyChanged(nameof(SourceName));
        RaisePropertyChanged(nameof(Version));
        RaisePropertyChanged(nameof(TotalNodeCount));
        RaisePropertyChanged(nameof(SensorCount));
        RaisePropertyChanged(nameof(IsLoading));
        RaisePropertyChanged(nameof(HasSnapshot));
        RaisePropertyChanged(nameof(HasError));
        RaisePropertyChanged(nameof(RejectionMessage));
        RaisePropertyChanged(nameof(ShowInitialEmptyState));
        RaisePropertyChanged(nameof(ShowInitialErrorState));
        RaisePropertyChanged(nameof(HasRetainedSnapshotAfterError));
        RaisePropertyChanged(nameof(SourceStatus));
        RaisePropertyChanged(nameof(VersionStatus));
        RaisePropertyChanged(nameof(NodeStatus));
        RaisePropertyChanged(nameof(SensorStatus));
        RaisePropertyChanged(nameof(LoadStateStatus));
    }

    private sealed record LoadRequest(long Id, CancellationTokenSource Cancellation);
}
