using System.Windows.Interop;

namespace EventGhostDotNet.Platform.Windows;

/// <summary>Fenêtre de messages uniquement, pour recevoir les notifications Windows.</summary>
internal sealed class MessageOnlyWindow : IDisposable
{
    private static readonly IntPtr MessageOnlyParent = new(-3);

    private readonly HwndSource _source;
    private readonly Action<int, IntPtr> _handler;

    public MessageOnlyWindow(string name, Action<int, IntPtr> handler)
    {
        _handler = handler;
        var parameters = new HwndSourceParameters(name)
        {
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            ParentWindow = MessageOnlyParent,
            Width = 0,
            Height = 0
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public IntPtr Handle => _source.Handle;

    public void Dispose()
    {
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        _handler(message, wParam);
        return IntPtr.Zero;
    }
}
