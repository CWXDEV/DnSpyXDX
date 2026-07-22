using DnSpyXDX.Application;
using Photino.NET;

namespace DnSpyXDX.Host;

/// <summary>Applies application zoom through Photino's native webview API.</summary>
public sealed class PhotinoZoomService : IApplicationZoomService
{
    private PhotinoWindow? window;
    public int ZoomPercent { get; private set; } = 100;

    public void Attach(PhotinoWindow mainWindow)
    {
        window = mainWindow;
        window.SetZoom(ZoomPercent);
    }

    public void SetZoom(int percent)
    {
        ZoomPercent = Math.Clamp(percent, 50, 200);
        window?.SetZoom(ZoomPercent);
    }
}
