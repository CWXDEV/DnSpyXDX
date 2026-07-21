using DecompilerApp.Application;
using Photino.NET;

namespace DecompilerApp.Host;

public sealed class PhotinoFileDialogService(PhotinoWindow window) : IFileDialogService
{
    public Task<string?> OpenAssemblyAsync()
    {
        var files = window.ShowOpenFile(
            "Open .NET assembly",
            multiSelect: false,
            filters:
            [
                ("All files (*.*)", ["*.*"])
            ]);
        return Task.FromResult(files.FirstOrDefault());
    }

    public Task<string?> SelectExportFolderAsync() => Task.FromResult(window.ShowOpenFolder("Choose an empty export folder").FirstOrDefault());
}
