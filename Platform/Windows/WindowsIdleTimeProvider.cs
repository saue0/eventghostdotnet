using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using EventGhostDotNet.PluginContracts;

namespace EventGhostDotNet.Platform.Windows;

internal sealed class WindowsIdleTimeProvider : IIdleTimeProvider, IDisposable
{
    private const long Unseeded = long.MinValue;
    private const long NoInputToken = -1;

    private readonly ISessionState _session;
    private readonly Action<Exception>? _logError;
    private long _activeSince = Unseeded;
    private long _generation;
    private long _lastInputToken = NoInputToken;
    private long _lastInputAt64 = Unseeded;
    private int _errorLogged;

    public WindowsIdleTimeProvider(ISessionState session, Action<Exception>? logError = null)
    {
        _session = session;
        _logError = logError;
        _session.StatusChanged += OnStatusChanged;
    }

    public IdleSnapshot GetIdleSnapshot()
    {
        if (_session.Status != SessionStatus.Active)
            return new IdleSnapshot(TimeSpan.Zero, Interlocked.Increment(ref _generation));

        var now64 = Environment.TickCount64;
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            ReportErrorOnce(new Win32Exception(Marshal.GetLastWin32Error()));
            return new IdleSnapshot(TimeSpan.Zero, Interlocked.Increment(ref _generation));
        }

        TrackLastInput(info.Time, now64);

        var idleMilliseconds = Math.Max(0, now64 - Interlocked.Read(ref _lastInputAt64));

        // Au démarrage, l'inactivité réelle est conservée ; après un déverrouillage
        // ou une reprise, le décompte repart de zéro.
        var activeSince = Interlocked.Read(ref _activeSince);
        if (activeSince != Unseeded)
            idleMilliseconds = Math.Min(idleMilliseconds, Math.Max(0, now64 - activeSince));

        var token = (Interlocked.Read(ref _generation) << 32) | info.Time;
        return new IdleSnapshot(TimeSpan.FromMilliseconds(idleMilliseconds), token);
    }

    public void Dispose() => _session.StatusChanged -= OnStatusChanged;

    private void TrackLastInput(uint inputToken, long now64)
    {
        if (Interlocked.Read(ref _lastInputToken) == inputToken)
            return;

        if (Interlocked.Read(ref _lastInputAt64) == Unseeded)
        {
            // GetLastInputInfo n'expose que 32 bits. Au premier relevé, l'inactivité
            // existante est reconstruite avec les 32 bits bas de TickCount64, qui
            // partagent l'horloge de GetLastInputInfo. Ensuite, seul TickCount64 est
            // utilisé : l'inactivité reste juste au-delà de 49,7 jours.
            var seed = Math.Min(unchecked((uint)now64 - inputToken), (ulong)now64);
            Interlocked.Exchange(ref _lastInputAt64, now64 - (long)seed);
        }
        else
            Interlocked.Exchange(ref _lastInputAt64, now64);

        Interlocked.Exchange(ref _lastInputToken, inputToken);
    }

    private void ReportErrorOnce(Exception exception)
    {
        if (Interlocked.Exchange(ref _errorLogged, 1) != 0)
            return;

        if (_logError is not null)
            _logError(exception);
        else
            Trace.TraceError($"Inactivité système indisponible : {exception}");
    }

    private void OnStatusChanged(SessionStatus status)
    {
        if (status == SessionStatus.Active)
            Interlocked.Exchange(ref _activeSince, Environment.TickCount64);
        Interlocked.Increment(ref _generation);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
}
