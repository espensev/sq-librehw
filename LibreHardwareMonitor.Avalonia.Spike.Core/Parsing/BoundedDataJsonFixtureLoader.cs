using System.Buffers;
using System.Security;
using System.Text.Json;
using LibreHardwareMonitor.Avalonia.Spike.Core.Contracts;

namespace LibreHardwareMonitor.Avalonia.Spike.Core.Parsing;

public sealed class BoundedDataJsonFixtureLoader : ISensorFixtureLoader
{
    private const int ReadBufferSize = 81_920;

    private readonly object _loadSync = new();
    private CancellationTokenSource? _activeLoad;

    public Task<SensorLoadResult> LoadAsync(
        Stream stream,
        string sourceName,
        SensorLoadLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(limits);

        string effectiveSourceName = string.IsNullOrWhiteSpace(sourceName)
            ? "(stream)"
            : sourceName;

        return RunLatestAsync(
            token => LoadStreamCoreAsync(stream, effectiveSourceName, limits, token),
            cancellationToken);
    }

    public Task<SensorLoadResult> LoadFileAsync(
        string filePath,
        SensorLoadLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(limits);

        return RunLatestAsync(
            token => LoadFileCoreAsync(filePath, limits, token),
            cancellationToken);
    }

    private async Task<SensorLoadResult> RunLatestAsync(
        Func<CancellationToken, Task<SensorLoadResult>> operation,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource supersessionSource = new();
        CancellationTokenSource? previousLoad;

        lock (_loadSync)
        {
            previousLoad = _activeLoad;
            _activeLoad = supersessionSource;
        }

        TryCancel(previousLoad);

        using CancellationTokenSource linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                supersessionSource.Token);

        try
        {
            SensorLoadResult result = await operation(linkedSource.Token).ConfigureAwait(false);
            linkedSource.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException)
        {
            return Failure(
                SensorLoadErrorCode.SupersededOrCancelled,
                "The fixture load was cancelled or superseded.");
        }
        finally
        {
            lock (_loadSync)
            {
                if (ReferenceEquals(_activeLoad, supersessionSource))
                {
                    _activeLoad = null;
                }
            }
        }
    }

    private static async Task<SensorLoadResult> LoadFileCoreAsync(
        string filePath,
        SensorLoadLimits limits,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(filePath))
            {
                return Failure(
                    SensorLoadErrorCode.IoFailure,
                    "The selected fixture file could not be read.");
            }

            FileInfo fileInfo = new(filePath);
            if (!fileInfo.Exists)
            {
                return Failure(
                    SensorLoadErrorCode.FileNotFound,
                    "The selected fixture file was not found.");
            }

            if (fileInfo.Length > limits.MaxInputBytes)
            {
                return Failure(
                    SensorLoadErrorCode.InputTooLarge,
                    "The fixture exceeds the configured input byte limit.");
            }

            await using FileStream stream = new(
                filePath,
                new FileStreamOptions
                {
                    Access = FileAccess.Read,
                    Mode = FileMode.Open,
                    Share = FileShare.Read,
                    BufferSize = ReadBufferSize,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                });

            return await LoadStreamCoreAsync(
                    stream,
                    GetSafeSourceName(filePath),
                    limits,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FileNotFoundException)
        {
            return Failure(
                SensorLoadErrorCode.FileNotFound,
                "The selected fixture file was not found.");
        }
        catch (DirectoryNotFoundException)
        {
            return Failure(
                SensorLoadErrorCode.FileNotFound,
                "The selected fixture file was not found.");
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(
                SensorLoadErrorCode.AccessDenied,
                "Access to the selected fixture file was denied.");
        }
        catch (SecurityException)
        {
            return Failure(
                SensorLoadErrorCode.AccessDenied,
                "Access to the selected fixture file was denied.");
        }
        catch (IOException)
        {
            return Failure(
                SensorLoadErrorCode.IoFailure,
                "The selected fixture file could not be read.");
        }
        catch (ArgumentException)
        {
            return Failure(
                SensorLoadErrorCode.IoFailure,
                "The selected fixture file could not be read.");
        }
        catch (NotSupportedException)
        {
            return Failure(
                SensorLoadErrorCode.IoFailure,
                "The selected fixture file could not be read.");
        }
    }

    private static async Task<SensorLoadResult> LoadStreamCoreAsync(
        Stream stream,
        string sourceName,
        SensorLoadLimits limits,
        CancellationToken cancellationToken)
    {
        if (!stream.CanRead)
        {
            return Failure(
                SensorLoadErrorCode.IoFailure,
                "The fixture stream could not be read.");
        }

        byte[] readBuffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);

        try
        {
            using MemoryStream payload = new();
            long totalBytesRead = 0;
            long probeLimit = limits.MaxInputBytes == long.MaxValue
                ? long.MaxValue
                : limits.MaxInputBytes + 1;

            while (totalBytesRead < probeLimit)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int requestedBytes = (int)Math.Min(
                    readBuffer.Length,
                    probeLimit - totalBytesRead);
                int bytesRead = await stream
                    .ReadAsync(readBuffer.AsMemory(0, requestedBytes), cancellationToken)
                    .ConfigureAwait(false);

                if (bytesRead == 0)
                {
                    break;
                }

                if (bytesRead < 0 || bytesRead > requestedBytes)
                {
                    return Failure(
                        SensorLoadErrorCode.IoFailure,
                        "The fixture stream could not be read.");
                }

                totalBytesRead += bytesRead;
                if (totalBytesRead > limits.MaxInputBytes)
                {
                    return Failure(
                        SensorLoadErrorCode.InputTooLarge,
                        "The fixture exceeds the configured input byte limit.");
                }

                payload.Write(readBuffer, 0, bytesRead);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return ParsePayload(
                payload.ToArray(),
                sourceName,
                limits,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(
                SensorLoadErrorCode.AccessDenied,
                "Access to the fixture stream was denied.");
        }
        catch (IOException)
        {
            return Failure(
                SensorLoadErrorCode.IoFailure,
                "The fixture stream could not be read.");
        }
        catch (NotSupportedException)
        {
            return Failure(
                SensorLoadErrorCode.IoFailure,
                "The fixture stream could not be read.");
        }
        catch (ObjectDisposedException)
        {
            return Failure(
                SensorLoadErrorCode.IoFailure,
                "The fixture stream could not be read.");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(readBuffer);
        }
    }

    private static SensorLoadResult ParsePayload(
        ReadOnlyMemory<byte> payload,
        string sourceName,
        SensorLoadLimits limits,
        CancellationToken cancellationToken)
    {
        SensorLoadError? validationError = ValidateJsonTokens(
            payload.Span,
            limits,
            cancellationToken);
        if (validationError is not null)
        {
            return SensorLoadResult.Failure(validationError);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(
                payload,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = limits.MaxDepth
                });

            cancellationToken.ThrowIfCancellationRequested();
            SensorSnapshot snapshot = DataJsonProjection.Project(
                document.RootElement,
                sourceName,
                limits,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return SensorLoadResult.Success(snapshot);
        }
        catch (SensorProjectionException exception)
        {
            return Failure(exception.Code, exception.OperatorMessage);
        }
        catch (JsonException)
        {
            return Failure(
                SensorLoadErrorCode.InvalidJson,
                "The fixture is not valid JSON.");
        }
    }

    private static SensorLoadError? ValidateJsonTokens(
        ReadOnlySpan<byte> payload,
        SensorLoadLimits limits,
        CancellationToken cancellationToken)
    {
        try
        {
            Utf8JsonReader reader = new(
                payload,
                new JsonReaderOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = limits.MaxDepth == int.MaxValue
                        ? int.MaxValue
                        : limits.MaxDepth + 1
                });

            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
                {
                    int containerDepth = reader.CurrentDepth + 1;
                    if (containerDepth > limits.MaxDepth)
                    {
                        return new SensorLoadError(
                            SensorLoadErrorCode.ExcessiveDepth,
                            "The fixture exceeds the configured JSON depth limit.");
                    }
                }

                if (reader.TokenType is JsonTokenType.PropertyName or JsonTokenType.String)
                {
                    string? value = reader.GetString();
                    if (value is not null && value.Length > limits.MaxStringCharacters)
                    {
                        return new SensorLoadError(
                            SensorLoadErrorCode.ExcessiveStringLength,
                            "The fixture contains a property name or string value that is too long.");
                    }
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return new SensorLoadError(
                SensorLoadErrorCode.InvalidJson,
                "The fixture is not valid JSON.");
        }
    }

    private static string GetSafeSourceName(string filePath)
    {
        try
        {
            string sourceName = Path.GetFileName(filePath);
            return string.IsNullOrWhiteSpace(sourceName)
                ? "(fixture file)"
                : sourceName;
        }
        catch (ArgumentException)
        {
            return "(fixture file)";
        }
    }

    private static void TryCancel(CancellationTokenSource? source)
    {
        if (source is null)
        {
            return;
        }

        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The earlier request completed between replacement and cancellation.
        }
    }

    private static SensorLoadResult Failure(
        SensorLoadErrorCode code,
        string message)
    {
        return SensorLoadResult.Failure(new SensorLoadError(code, message));
    }
}
