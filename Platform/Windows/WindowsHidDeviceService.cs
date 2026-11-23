using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using EventGhostDotNet.PluginContracts;
using Microsoft.Win32.SafeHandles;

namespace EventGhostDotNet.Platform.Windows;

internal sealed class WindowsHidDeviceService : IHidDeviceService, IDisposable
{
    private const int WmDeviceChange = 0x0219;
    private const int DbtDeviceArrival = 0x8000;
    private const int DbtDeviceRemoveComplete = 0x8004;
    private const int DbtDevNodesChanged = 0x0007;
    private const int DeviceTypeDeviceInterface = 5;

    private const uint DevicePresent = 0x00000002;
    private const uint DeviceInterface = 0x00000010;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint Overlapped = 0x40000000;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    private readonly object _sync = new();
    private readonly MessageOnlyWindow? _window;
    private readonly IntPtr _notification;
    private IReadOnlyList<string>? _cache;
    private int _disposed;

    public WindowsHidDeviceService()
    {
        try
        {
            _window = new MessageOnlyWindow("EventGhostDotNet.HidNotifications", OnWindowMessage);
            HidD_GetHidGuid(out var hidGuid);
            var filter = new DeviceBroadcastInterface
            {
                Size = Marshal.SizeOf<DeviceBroadcastInterface>(),
                DeviceType = DeviceTypeDeviceInterface,
                ClassGuid = hidGuid
            };
            _notification = RegisterDeviceNotification(_window.Handle, ref filter, 0);
            if (_notification == IntPtr.Zero)
            {
                Trace.TraceError(
                    $"Notification HID indisponible : {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
                _window.Dispose();
                _window = null;
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or
                                           ArgumentException or NotSupportedException)
        {
            // Sans fenêtre de notification, la liste reste réénumérée à chaque appel.
            Trace.TraceError($"Notification HID indisponible : {exception.Message}");
            _window?.Dispose();
            _window = null;
            _notification = IntPtr.Zero;
        }
    }

    /// <summary>Déclenché sur le thread d'interface quand une interface HID apparaît ou disparaît.</summary>
    public event Action? DevicesChanged;

    public IReadOnlyList<string> EnumerateDevicePaths()
    {
        if (_notification == IntPtr.Zero)
            return EnumerateDevicePathsCore();

        lock (_sync)
            return _cache ??= EnumerateDevicePathsCore();
    }

    public SafeFileHandle Open(string devicePath)
    {
        var handle = CreateFile(devicePath, GenericRead | GenericWrite, 3, IntPtr.Zero, OpenExisting, Overlapped, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, "Impossible d'ouvrir le récepteur Logitech.");
        }
        return handle;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (_notification != IntPtr.Zero)
            UnregisterDeviceNotification(_notification);
        _window?.Dispose();
    }

    private void OnWindowMessage(int message, IntPtr wParam)
    {
        if (message != WmDeviceChange)
            return;

        var kind = wParam.ToInt32();
        if (kind is not (DbtDeviceArrival or DbtDeviceRemoveComplete or DbtDevNodesChanged))
            return;

        lock (_sync)
            _cache = null;
        DevicesChanged?.Invoke();
    }

    private static IReadOnlyList<string> EnumerateDevicePathsCore()
    {
        HidD_GetHidGuid(out var hidGuid);
        var info = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DevicePresent | DeviceInterface);
        if (info == InvalidHandleValue)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Impossible d'énumérer les interfaces HID.");

        try
        {
            var paths = new List<string>();
            for (uint index = 0; ; index++)
            {
                var interfaceData = new DeviceInterfaceData { Size = Marshal.SizeOf<DeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(info, IntPtr.Zero, ref hidGuid, index, ref interfaceData))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 259)
                        break;
                    throw new Win32Exception(error, "Impossible de lire la liste des périphériques HID.");
                }

                _ = SetupDiGetDeviceInterfaceDetail(info, ref interfaceData, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);
                var detail = Marshal.AllocHGlobal(checked((int)requiredSize));
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(info, ref interfaceData, detail, requiredSize, out _, IntPtr.Zero))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Impossible de lire le chemin d'un périphérique HID.");
                    paths.Add(Marshal.PtrToStringUni(IntPtr.Add(detail, 4)) ?? string.Empty);
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
            return paths;
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(info);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterfaceData
    {
        public int Size;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceBroadcastInterface
    {
        public int Size;
        public int DeviceType;
        public int Reserved;
        public Guid ClassGuid;
        public short Name;
    }

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid, IntPtr enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid,
        uint memberIndex, ref DeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet, ref DeviceInterfaceData deviceInterfaceData,
        IntPtr detailData, uint detailDataSize, out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterDeviceNotification(
        IntPtr recipient, ref DeviceBroadcastInterface filter, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterDeviceNotification(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
}
