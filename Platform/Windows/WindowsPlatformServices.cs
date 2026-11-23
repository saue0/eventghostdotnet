using EventGhostDotNet.PluginContracts;

namespace EventGhostDotNet.Platform.Windows;

internal sealed class WindowsPlatformServices : IDisposable
{
    private readonly WindowsSessionState _session;
    private readonly WindowsMonitorService _monitors = new();
    private readonly WindowsHidDeviceService _hidDevices = new();

    public WindowsPlatformServices(Action<Exception>? logError = null)
    {
        _session = new WindowsSessionState();
        Session = _session;
        IdleTime = new WindowsIdleTimeProvider(_session, logError);
    }

    public ISessionState Session { get; }
    public IIdleTimeProvider IdleTime { get; }
    public IMonitorService Monitors => _monitors;
    public IHidDeviceService HidDevices => _hidDevices;

    public void Dispose()
    {
        _hidDevices.Dispose();
        _monitors.Dispose();
        (IdleTime as IDisposable)?.Dispose();
        _session.Dispose();
    }
}
