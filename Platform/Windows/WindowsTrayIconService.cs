using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using EventGhostDotNet.PluginContracts;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Icon = System.Drawing.Icon;

namespace EventGhostDotNet.Platform.Windows;

internal sealed class WindowsTrayIconService : ITrayIconService
{
    private const int TooltipLimit = 63;
    private static readonly int TaskbarCreated = RegisterWindowMessage("TaskbarCreated");

    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly MessageOnlyWindow? _window;
    private Forms.NotifyIcon? _icon;
    private Icon? _image;
    private Forms.ContextMenuStrip? _menu;
    private string _iconPath = string.Empty;
    private string _tooltip = string.Empty;
    private bool _disposed;

    public WindowsTrayIconService()
    {
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

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

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
}
