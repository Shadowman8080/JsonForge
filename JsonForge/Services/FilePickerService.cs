using System.Text;
using JsonForge.Models;
using Microsoft.UI.Xaml;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace JsonForge.Services;

public interface IFilePickerService
{
    Task<(StorageFile File, string Token)?> PickOpenAsync();

    Task<(StorageFile File, string Token)?> PickSaveAsync(string suggestedFileName);

    Task<string> ReadTextAsync(StorageFile file);

    Task WriteTextAsync(StorageFile file, string text);

    Task<StorageFile?> TryGetFromTokenAsync(string token);
}

public sealed class FilePickerService : IFilePickerService
{
    public async Task<(StorageFile File, string Token)?> PickOpenAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        picker.FileTypeFilter.Add(".jsonc");
        picker.FileTypeFilter.Add(".ndjson");
        picker.FileTypeFilter.Add(".txt");
        picker.FileTypeFilter.Add(".*");

        InitializeWithCurrentWindowIfPossible(picker);

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return null;
        }

        var token = CreateReopenToken(file);
        return (file, token);
    }

    public async Task<(StorageFile File, string Token)?> PickSaveAsync(string suggestedFileName)
    {
        var picker = new FileSavePicker
        {
            SuggestedFileName = suggestedFileName,
        };
        picker.FileTypeChoices.Add("JSON", new List<string> { ".json" });
        picker.FileTypeChoices.Add("JSONC", new List<string> { ".jsonc" });
        picker.FileTypeChoices.Add("Text", new List<string> { ".txt" });

        InitializeWithCurrentWindowIfPossible(picker);

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return null;
        }

        var token = CreateReopenToken(file);
        return (file, token);
    }

    public async Task<string> ReadTextAsync(StorageFile file)
    {
        // Ensure stable newlines for the editor.
        var text = await FileIO.ReadTextAsync(file, Windows.Storage.Streams.UnicodeEncoding.Utf8);
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    public Task WriteTextAsync(StorageFile file, string text)
        => FileIO.WriteTextAsync(file, text, Windows.Storage.Streams.UnicodeEncoding.Utf8).AsTask();

    public async Task<StorageFile?> TryGetFromTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        // Try FutureAccessList first (on Windows/UWP/WinAppSDK).
        try
        {
            var file = await TryGetFromFutureAccessListAsync(token);
            if (file is not null)
            {
                return file;
            }
        }
        catch
        {
            // ignore
        }

        // Fallback: treat token as a file path.
        try
        {
            return await StorageFile.GetFileFromPathAsync(token);
        }
        catch
        {
            return null;
        }
    }

    private static string CreateReopenToken(StorageFile file)
    {
        // Prefer a FutureAccessList token where available; otherwise fall back to the file path.
        try
        {
            var token = TryAddToFutureAccessList(file);
            if (!string.IsNullOrWhiteSpace(token))
            {
                return token;
            }
        }
        catch
        {
            // ignore
        }

        return file.Path ?? string.Empty;
    }

    private static string? TryAddToFutureAccessList(StorageFile file)
    {
        // Reflection avoids Uno "not implemented" compile-time warnings and runtime failures.
        var permissionsType = Type.GetType("Windows.Storage.AccessCache.StorageApplicationPermissions, Windows");
        if (permissionsType is null)
        {
            return null;
        }

        var futureAccessListProp = permissionsType.GetProperty("FutureAccessList");
        var futureAccessList = futureAccessListProp?.GetValue(null);
        if (futureAccessList is null)
        {
            return null;
        }

        var add = futureAccessList.GetType().GetMethod("Add", [typeof(Windows.Storage.IStorageItem)]);
        if (add is null)
        {
            return null;
        }

        return add.Invoke(futureAccessList, [file]) as string;
    }

    private static Task<StorageFile?> TryGetFromFutureAccessListAsync(string token)
    {
        var permissionsType = Type.GetType("Windows.Storage.AccessCache.StorageApplicationPermissions, Windows");
        if (permissionsType is null)
        {
            return Task.FromResult<StorageFile?>(null);
        }

        var futureAccessListProp = permissionsType.GetProperty("FutureAccessList");
        var futureAccessList = futureAccessListProp?.GetValue(null);
        if (futureAccessList is null)
        {
            return Task.FromResult<StorageFile?>(null);
        }

        var getFileAsync = futureAccessList.GetType().GetMethod("GetFileAsync", [typeof(string)]);
        if (getFileAsync is null)
        {
            return Task.FromResult<StorageFile?>(null);
        }

        var op = getFileAsync.Invoke(futureAccessList, [token]);
        if (op is null)
        {
            return Task.FromResult<StorageFile?>(null);
        }

        // IAsyncOperation<StorageFile> -> AsTask() extension exists on WinRT; use reflection.
        var asTask = op.GetType().GetMethod("AsTask", Type.EmptyTypes);
        if (asTask is not null)
        {
            var taskObj = asTask.Invoke(op, null);
            if (taskObj is Task<StorageFile> task)
            {
                return task.ContinueWith<StorageFile?>(t => t.Status == TaskStatus.RanToCompletion ? t.Result : null);
            }
        }

        return Task.FromResult<StorageFile?>(null);
    }

    private static void InitializeWithCurrentWindowIfPossible(object picker)
    {
        // WinUI requires initializing pickers with an HWND.
        // Use reflection so this compiles/runs across all Uno targets.
        try
        {
            if (Application.Current is not JsonForge.App app || app.MainWindow is null)
            {
                return;
            }

            var window = app.MainWindow;

            var windowNative = Type.GetType("WinRT.Interop.WindowNative, WinRT.Runtime");
            var initWithWindow = Type.GetType("WinRT.Interop.InitializeWithWindow, WinRT.Runtime");
            if (windowNative is null || initWithWindow is null)
            {
                return;
            }

            var getHwnd = windowNative.GetMethod("GetWindowHandle");
            var initialize = initWithWindow.GetMethod("Initialize");
            if (getHwnd is null || initialize is null)
            {
                return;
            }

            var hwnd = getHwnd.Invoke(null, [window]);
            if (hwnd is null)
            {
                return;
            }

            initialize.Invoke(null, [picker, hwnd]);
        }
        catch
        {
            // ignore
        }
    }
}
