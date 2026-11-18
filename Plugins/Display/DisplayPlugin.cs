using System.ComponentModel;
using System.Runtime.InteropServices;

namespace EventGhostDotNet.Plugins.Display;

internal sealed record DisplayMonitor(string Key, string Label);

internal static class DisplayPluginService
{
    private const uint MonitorInfoPrimary = 1;
    private const byte VcpInputSource = 0x60;

    public static IReadOnlyList<DisplayMonitor> GetMonitors()
    {
        var result = new List<DisplayMonitor>();
        EnumerateMonitors((monitor, info) =>
        {
            if (!TryGetPhysicalMonitors(monitor, out var physicalMonitors))
                return true;

            try
            {
                for (var index = 0; index < physicalMonitors.Length; index++)
                {
                    var primary = (info.Flags & MonitorInfoPrimary) != 0 ? " (principal)" : string.Empty;
                    result.Add(new DisplayMonitor(
                        $"{info.DeviceName}|{index}",
                        $"{info.DeviceName}{primary} — {physicalMonitors[index].Description.Trim()}"));
                }
            }
            finally
            {
                DestroyPhysicalMonitors(physicalMonitors);
            }

            return true;
        });
        return result;
    }

    public static void SetInputSource(string monitorKey, uint inputSource)
    {
        var found = false;
        EnumerateMonitors((monitor, info) =>
        {
            var prefix = $"{info.DeviceName}|";
            if (!monitorKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(monitorKey.AsSpan(prefix.Length), out var requestedIndex))
                return true;

            if (!TryGetPhysicalMonitors(monitor, out var physicalMonitors))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Impossible d'accéder aux moniteurs physiques.");

            try
            {
                if (requestedIndex < 0 || requestedIndex >= physicalMonitors.Length)
                    throw new InvalidOperationException("Le moniteur configuré n'est plus disponible.");

                if (!SetVcpFeature(physicalMonitors[requestedIndex].Handle, VcpInputSource, inputSource))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Le moniteur a refusé la commande DDC/CI.");
                found = true;
            }
            finally
            {
                DestroyPhysicalMonitors(physicalMonitors);
            }
            return true;
        });

        if (!found)
            throw new InvalidOperationException($"Moniteur introuvable ou non compatible DDC/CI : {monitorKey}");
    }

    private static void EnumerateMonitors(Func<IntPtr, MonitorInfoEx, bool> callback)
    {
        Exception? callbackException = null;
        MonitorEnumProc nativeCallback = (monitor, _, _, _) =>
        {
            try
            {
                var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>(), DeviceName = string.Empty };
                if (!GetMonitorInfo(monitor, ref info))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Impossible de lire les informations du moniteur.");
                return callback(monitor, info);
            }
            catch (Exception exception)
            {
                callbackException = exception;
                return false;
            }
        };

        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, nativeCallback, IntPtr.Zero) && callbackException is null)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Impossible d'énumérer les écrans.");
        if (callbackException is not null)
            throw callbackException;
    }

    private static bool TryGetPhysicalMonitors(IntPtr monitor, out PhysicalMonitor[] physicalMonitors)
    {
        physicalMonitors = [];
        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) || count == 0)
            return false;

        physicalMonitors = new PhysicalMonitor[count];
        if (GetPhysicalMonitorsFromHMONITOR(monitor, count, physicalMonitors))
            return true;

        physicalMonitors = [];
        return false;
    }

    private static void DestroyPhysicalMonitors(IEnumerable<PhysicalMonitor> physicalMonitors)
    {
        foreach (var physicalMonitor in physicalMonitors)
        {
            if (!DestroyPhysicalMonitor(physicalMonitor.Handle))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Impossible de libérer le handle du moniteur.");
        }
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr deviceContext, IntPtr rect, IntPtr userData);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitor
    {
        public IntPtr Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clipRect, MonitorEnumProc callback, IntPtr userData);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(
        IntPtr monitor, uint count, [Out] PhysicalMonitor[] physicalMonitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitor(IntPtr monitor);

    [DllImport("dxva2.dll", EntryPoint = "SetVCPFeature", SetLastError = true)]
    private static extern bool SetVcpFeature(IntPtr monitor, byte code, uint value);
}
