using Microsoft.Win32.SafeHandles;

namespace EventGhostDotNet.PluginContracts;

public enum SessionStatus
{
    Active,
    Locked,
    Suspended
}

/// <summary>État de la session Windows : verrouillage et veille.</summary>
public interface ISessionState
{
    SessionStatus Status { get; }

    /// <summary>Déclenché depuis le thread des événements système.</summary>
    event Action<SessionStatus>? StatusChanged;
}

/// <summary>État d'inactivité système. <paramref name="InputToken"/> change à chaque entrée utilisateur.</summary>
public sealed record IdleSnapshot(TimeSpan IdleTime, long InputToken);

public interface IIdleTimeProvider
{
    IdleSnapshot GetIdleSnapshot();
}

public sealed record PlatformMonitor(string Key, string Label);

public interface IMonitorService
{
    IReadOnlyList<PlatformMonitor> GetMonitors();
    void SetInputSource(string monitorKey, uint inputSource);
}

public interface IHidDeviceService
{
    /// <summary>Chemins des interfaces HID présentes ; le filtrage par modèle reste au plugin.</summary>
    IReadOnlyList<string> EnumerateDevicePaths();

    SafeFileHandle Open(string devicePath);

    /// <summary>Déclenché quand une interface HID apparaît ou disparaît.</summary>
    event Action? DevicesChanged;
}

public sealed record TrayMenuItem(string Label, Action? Invoke, bool IsSeparator = false)
{
    public static TrayMenuItem Separator { get; } = new(string.Empty, null, true);
}

public interface ITrayIconService : IDisposable
{
    event Action? Activated;

    void Show(string iconPath, string tooltip);

    void SetMenu(IReadOnlyList<TrayMenuItem> items);
}
