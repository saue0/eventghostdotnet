using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using EventGhostDotNet.PluginContracts;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Icon = System.Drawing.Icon;

namespace EventGhostDotNet.Platform.Windows;

internal sealed class WindowsTrayIconService : ITrayIconService
{
    private const int TooltipLimit = 63;
    private const int HotkeyId = 1;
    private const int WmHotkey = 0x0312;
    private const uint HotkeyModifiers = 0x0001 | 0x0002 | 0x4000; // Alt | Control | NoRepeat
    private const uint HotkeyVirtualKey = 0x45; // E
    private static readonly int TaskbarCreated = RegisterWindowMessage("TaskbarCreated");

    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly MessageOnlyWindow? _window;
    private readonly Action<string, LogLevel>? _log;
    private bool _hotkeyRegistered;
    private Forms.NotifyIcon? _icon;
    private Icon? _image;
    private Forms.ContextMenuStrip? _menu;
    private string _iconPath = string.Empty;
    private string _tooltip = string.Empty;
    private bool _disposed;

    public WindowsTrayIconService(Action<string, LogLevel>? log = null)
    {
        _log = log;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        try
        {
            _window = new MessageOnlyWindow("EventGhostDotNet.TrayIcon", OnWindowMessage);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or
                                           ArgumentException or NotSupportedException)
        {
            // NotifyIcon recrée déjà l'icône après un redémarrage de l'Explorateur ;
            // sans fenêtre, il reste le seul mécanisme de reprise.
            Trace.TraceError($"Fenêtre de notification de l'icône indisponible : {exception.Message}");
            _window = null;
        }

        if (_window is not null)
        {
            _hotkeyRegistered = RegisterHotKey(_window.Handle, HotkeyId, HotkeyModifiers, HotkeyVirtualKey);
            _log?.Invoke(
                _hotkeyRegistered
                    ? "Raccourci global Ctrl+Alt+E du menu de l'icône enregistré."
                    : "Raccourci global Ctrl+Alt+E du menu de l'icône indisponible : déjà utilisé par une autre application.",
                _hotkeyRegistered ? LogLevel.Debug : LogLevel.Warning);
        }
    }

    public event Action? Activated;

    public void Show(string iconPath, string tooltip)
    {
        if (_icon is not null)
            throw new InvalidOperationException("L'icône de notification est déjà affichée.");

        _iconPath = iconPath;
        _tooltip = tooltip;
        _image = LoadIcon();
        var icon = new Forms.NotifyIcon
        {
            Icon = _image,
            Text = LimitTooltip(tooltip)
        };
        icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                Activated?.Invoke();
        };
        _icon = icon;
        icon.Visible = true;
    }

    public void SetMenu(IReadOnlyList<TrayMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (_icon is null)
            throw new InvalidOperationException("L'icône de notification n'est pas affichée.");

        var menu = new Forms.ContextMenuStrip();
        foreach (var item in items)
        {
            if (item.IsSeparator)
                menu.Items.Add(new Forms.ToolStripSeparator());
            else
                menu.Items.Add(new Forms.ToolStripMenuItem(item.Label, null, (_, _) => item.Invoke?.Invoke()));
        }

        var previousMenu = _menu;
        _icon.ContextMenuStrip = menu;
        _menu = menu;
        previousMenu?.Dispose();
    }

    /// <summary>
    /// Affiche le menu près de la zone de notification pour une activation au clavier ;
    /// le menu WinForms gère ensuite les flèches, Entrée et Échap.
    /// </summary>
    public void ShowMenu()
    {
        var menu = _menu;
        if (menu is null || menu.Visible)
            return;

        var area = Forms.Screen.PrimaryScreen?.WorkingArea ?? Forms.SystemInformation.VirtualScreen;
        var size = menu.GetPreferredSize(Drawing.Size.Empty);
        menu.Show(new Drawing.Point(area.Right - size.Width - 8, area.Bottom - size.Height - 8));
        menu.Focus();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        if (_hotkeyRegistered && _window is not null)
        {
            UnregisterHotKey(_window.Handle, HotkeyId);
            _hotkeyRegistered = false;
        }

        var icon = _icon;
        var image = _image;
        var menu = _menu;
        _icon = null;
        _image = null;
        _menu = null;

        if (icon is not null)
        {
            icon.Visible = false;
            icon.ContextMenuStrip = null;
        }
        icon?.Dispose();
        image?.Dispose();
        menu?.Dispose();
        _window?.Dispose();
    }

    private void OnWindowMessage(int message, IntPtr wParam)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            ShowMenu();
            return;
        }

        if (TaskbarCreated == 0 || message != TaskbarCreated || _icon is null)
            return;

        RefreshIcon();
        _icon.Visible = false;
        _icon.Visible = true;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // SystemEvents notifie depuis un thread système : l'icône appartient au thread d'interface.
        if (_context is null)
            return;
        _context.Post(_ => RefreshIcon(), null);
    }

    private void RefreshIcon()
    {
        if (_icon is null)
            return;

        // Après un changement de DPI ou de résolution, la taille demandée au shell change.
        var previous = _image;
        _image = LoadIcon();
        _icon.Icon = _image;
        _icon.Text = LimitTooltip(_tooltip);
        previous?.Dispose();
    }

    private Icon LoadIcon() => new(_iconPath, Forms.SystemInformation.SmallIconSize);

    private static string LimitTooltip(string tooltip)
    {
        var normalized = tooltip.Trim();
        return normalized.Length <= TooltipLimit ? normalized : normalized[..TooltipLimit];
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegisterWindowMessage(string message);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
