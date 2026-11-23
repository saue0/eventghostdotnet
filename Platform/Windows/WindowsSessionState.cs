using EventGhostDotNet.PluginContracts;
using Microsoft.Win32;

namespace EventGhostDotNet.Platform.Windows;

internal sealed class WindowsSessionState : ISessionState, IDisposable
{
    private readonly object _sync = new();
    private bool _locked;
    private bool _suspended;

    public WindowsSessionState()
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    /// <summary>Déclenché depuis le thread des événements système.</summary>
    public event Action<SessionStatus>? StatusChanged;

    public SessionStatus Status
    {
        get
        {
            lock (_sync)
                return Current;
        }
    }

    private SessionStatus Current =>
        _suspended ? SessionStatus.Suspended : _locked ? SessionStatus.Locked : SessionStatus.Active;

    public void Dispose()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteDisconnect:
                Update(locked: true);
                break;
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteConnect:
                Update(locked: false);
                break;
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                Update(suspended: true);
                break;
            case PowerModes.Resume:
                Update(suspended: false);
                break;
        }
    }

    private void Update(bool? locked = null, bool? suspended = null)
    {
        SessionStatus previous;
        SessionStatus current;
        lock (_sync)
        {
            previous = Current;
            if (locked.HasValue)
                _locked = locked.Value;
            if (suspended.HasValue)
                _suspended = suspended.Value;
            current = Current;
        }

        if (previous != current)
            StatusChanged?.Invoke(current);
    }
}
