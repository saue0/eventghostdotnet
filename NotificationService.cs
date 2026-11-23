using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace EventGhostDotNet;

/// <summary>Notification affichée pendant la session, conservée pour l'historique.</summary>
public sealed record NotificationHistoryEntry(string Title, string Message, DateTimeOffset Timestamp)
{
    public string Time => Timestamp.ToString("HH:mm:ss");
}

/// <summary>
/// Affiche les notifications dans le coin inférieur droit de l'écran principal,
/// sans dépendre du centre de notifications Windows ni de Windows App SDK.
/// </summary>
internal sealed class NotificationService : IDisposable
{
    private const int MaxVisible = 3;
    private const int MaxHistory = 50;
    private const double ScreenMargin = 16;
    private const double Spacing = 8;
    private const uint MonitorDefaultToPrimary = 1;

    private readonly Dispatcher _dispatcher;
    private readonly List<NotificationWindow> _windows = [];
    private readonly List<NotificationHistoryEntry> _history = [];
    private bool _positioning;
    private bool _disposed;

    public NotificationService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        // Une résolution, une barre des tâches ou une mise à l'échelle modifiée
        // déplace la zone de travail : l'empilement doit être recalculé.
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    /// <summary>Notifications de la session, de la plus ancienne à la plus récente.</summary>
    public IReadOnlyList<NotificationHistoryEntry> History => _history;

    public void Show(string title, string message, string? key, Action? activate = null)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => Show(title, message, key, activate));
            return;
        }
        if (_disposed)
            return;

        var normalizedKey = string.IsNullOrEmpty(key) ? null : key;
        if (normalizedKey is not null &&
            _windows.FirstOrDefault(window => string.Equals(window.Key, normalizedKey, StringComparison.Ordinal))
            is { } existing)
        {
            existing.UpdateContent(title, message);
            Remember(title, message);
            Reposition();
            return;
        }

        while (_windows.Count >= MaxVisible)
            _windows[0].Close();

        var window = new NotificationWindow(title, message, normalizedKey);
        window.ActivatedByUser += activate;
        window.SizeChanged += (_, _) => Reposition();
        window.DpiChanged += (_, _) => Reposition();
        window.Closed += (_, _) =>
        {
            _windows.Remove(window);
            Reposition();
        };
        _windows.Add(window);
        Remember(title, message);
        window.Show();
        Reposition();
    }

    public void CloseAll()
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(CloseAll);
            return;
        }

        foreach (var window in _windows.ToArray())
            window.Close();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        CloseAll();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => _dispatcher.BeginInvoke(Reposition);

    private void Remember(string title, string message)
    {
        // Une notification répétée ne doit pas saturer l'historique : seule
        // l'heure de la dernière occurrence est mise à jour.
        if (_history.Count > 0 && _history[^1].Title == title && _history[^1].Message == message)
        {
            _history[^1] = new NotificationHistoryEntry(title, message, DateTimeOffset.Now);
            return;
        }

        _history.Add(new NotificationHistoryEntry(title, message, DateTimeOffset.Now));
        if (_history.Count > MaxHistory)
            _history.RemoveAt(0);
    }

    private void Reposition()
    {
        if (_positioning || _disposed || _windows.Count == 0)
            return;

        _positioning = true;
        try
        {
            // L'écran principal et son facteur DPI sont relus à chaque passage :
            // aucune valeur n'est mise en cache entre deux changements d'affichage.
            var area = GetPrimaryWorkArea(_windows[^1]);
            var bottom = area.Bottom - ScreenMargin;
            foreach (var window in _windows.ToArray())
            {
                window.UpdateLayout();
                var width = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
                var height = window.ActualHeight > 0 ? window.ActualHeight : window.Height;
                window.Left = area.Right - width - ScreenMargin;
                window.Top = bottom - height;
                bottom = window.Top - Spacing;
            }
        }
        finally
        {
            _positioning = false;
        }
    }

    /// <summary>Zone de travail de l'écran principal, exprimée dans les unités de la fenêtre.</summary>
    private static Rect GetPrimaryWorkArea(Window window)
    {
        var monitor = MonitorFromPoint(new NativePoint(), MonitorDefaultToPrimary);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
        {
            // MONITORINFO renvoie des pixels physiques ; Window.Left/Top sont en DIP.
            var dpi = VisualTreeHelper.GetDpi(window);
            return new Rect(
                info.Work.Left / dpi.DpiScaleX,
                info.Work.Top / dpi.DpiScaleY,
                (info.Work.Right - info.Work.Left) / dpi.DpiScaleX,
                (info.Work.Bottom - info.Work.Top) / dpi.DpiScaleY);
        }

        // Repli : la zone de travail WPF est déjà en DIP de l'écran principal.
        return SystemParameters.WorkArea;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
