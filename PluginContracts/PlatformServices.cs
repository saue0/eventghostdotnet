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

/// <summary>Valeur courante et maximale d'un contrôle VCP lu par DDC/CI.</summary>
public sealed record MonitorVcpValue(uint Current, uint Maximum);

public interface IMonitorService
{
    IReadOnlyList<PlatformMonitor> GetMonitors();

    /// <summary>Lit un code VCP (par exemple <c>0x10</c> luminosité ou <c>0x60</c> entrée).</summary>
    MonitorVcpValue GetVcpFeature(string monitorKey, byte code);

    void SetVcpFeature(string monitorKey, byte code, uint value);

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

    /// <summary>Affiche le menu de l'icône à l'écran, pour une activation au clavier.</summary>
    void ShowMenu();
}
