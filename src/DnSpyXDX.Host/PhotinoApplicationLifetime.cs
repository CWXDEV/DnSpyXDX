using DnSpyXDX.Application;
using Photino.NET;

namespace DnSpyXDX.Host;

public sealed class PhotinoApplicationLifetime : IApplicationLifetime
{
    private PhotinoWindow? window;

    public void Attach(PhotinoWindow mainWindow) => window = mainWindow;

    public void Exit() => window?.Close();
}
