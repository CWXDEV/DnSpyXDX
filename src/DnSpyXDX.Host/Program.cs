using DnSpyXDX.Application;
using DnSpyXDX.Decompilation;
using DnSpyXDX.Export;
using DnSpyXDX.Host;
using DnSpyXDX.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Photino.Blazor;

internal static class Program
{
    // WebView2 can only be created from an STA thread
    [STAThread]
    private static void Main(string[] args)
    {
        var builder = PhotinoBlazorAppBuilder.CreateDefault(args);
        builder.Services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.AddFilter((category, _) => category?.StartsWith("DnSpyXDX", StringComparison.Ordinal) == true);
        });
        builder.Services.AddSingleton<IDecompilerBackend, DecompilerBackend>();
        builder.Services.AddSingleton<IProjectExportService, ProjectExportService>();
        builder.Services.AddSingleton<WorkspaceState>();
        builder.Services.AddSingleton<IFileDialogService, PhotinoFileDialogService>();
        builder.Services.AddSingleton<IWorkspaceSessionService, WorkspaceSessionService>();
        builder.RootComponents.Add<App>("app");
        var app = builder.Build();
        app.MainWindow.SetLogVerbosity(0).SetTitle("DnSpyXDX").SetIconFile(Path.Combine(AppContext.BaseDirectory, "wwwroot", "dnspyxdx.png")).SetSize(1320, 840).SetMinSize(860, 560).SetUseOsDefaultSize(false);
        WindowStateManager.Attach(app.MainWindow);
        app.Run();
    }
}
