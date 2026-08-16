using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace LibreHardwareMonitor.Avalonia.Spike.Services;

public interface IFilePickerFixtureSource
{
    Task<string?> PickFixtureAsync(Window owner);
}

public sealed class FilePickerFixtureSource : IFilePickerFixtureSource
{
    private static readonly FilePickerFileType _jsonFileType = new("JSON data")
    {
        Patterns = ["*.json"],
        MimeTypes = ["application/json"],
        AppleUniformTypeIdentifiers = ["public.json"],
    };

    public async Task<string?> PickFixtureAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (!owner.StorageProvider.CanOpen)
        {
            return null;
        }

        IReadOnlyList<IStorageFile> selectedFiles =
            await owner.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Open a read-only data.json fixture",
                    AllowMultiple = false,
                    FileTypeFilter = [_jsonFileType],
                    SuggestedFileType = _jsonFileType,
                });

        if (selectedFiles.Count != 1)
        {
            return null;
        }

        string? path = selectedFiles[0].TryGetLocalPath();
        return path is not null &&
            File.Exists(path) &&
            Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)
                ? path
                : null;
    }
}
