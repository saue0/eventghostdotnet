using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace EventGhostDotNet.Plugins.Logitech;

internal sealed record LogitechReceiver(string Path, string Label);

internal sealed class LogitechUnifyClient : IAsyncDisposable
{
    private static readonly IReadOnlyDictionary<ushort, string> KeyNames = new Dictionary<ushort, string>
    {
        [0x0034] = "Fn", [0x00D1] = "Host 1", [0x00D2] = "Host 2", [0x00D3] = "Host 3",
        [0x00D5] = "F1", [0x00D6] = "F2", [0x00BD] = "F3", [0x00D4] = "F4",
        [0x00C2] = "F11", [0x00DD] = "F12", [0x0006] = "Rew", [0x0004] = "Play/Pause",
        [0x0005] = "Fast", [0x0003] = "Mute", [0x0002] = "VolDn", [0x0001] = "VolUp",
        [0x000A] = "PgUp", [0x01A0] = "ActionsRing", [0x00C3] = "Gesture",
        [0x0053] = "Back", [0x0056] = "Forward", [0x00C4] = "WheelToggle"
    };

    private static readonly IReadOnlyDictionary<byte, string> BatteryStates = new Dictionary<byte, string>
    {
        [0] = "discharging", [1] = "recharging", [2] = "almost_full", [3] = "full",
        [4] = "slow_recharge", [5] = "invalid_battery", [6] = "thermal_error"
    };

    private readonly string _devicePath;
    private readonly string _eventPrefix;
    private readonly Action<string, string?> _eventCallback;
    private readonly Action<string> _logCallback;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _streamLock = new();
    private readonly Dictionary<byte, byte> _deviceFeatures = [];
    private readonly Dictionary<byte, byte> _keyFeatures = [];
    private readonly Dictionary<byte, byte> _batteryFeatures = [];
    private FileStream? _stream;
    private Task? _worker;

    public LogitechUnifyClient(
        LogitechReceiver receiver,
        string eventPrefix,
        Action<string, string?> eventCallback,
        Action<string> logCallback)
    {
        _devicePath = receiver.Path;
        _eventPrefix = string.IsNullOrWhiteSpace(eventPrefix) ? "Logitech" : eventPrefix.Trim();
        _eventCallback = eventCallback;
        _logCallback = logCallback;
    }

    public static IReadOnlyList<LogitechReceiver> FindReceivers() => LogitechHidNative.FindReceivers();

    public void Start()
    {
        if (_worker is not null)
            throw new InvalidOperationException("Le récepteur Logitech est déjà démarré.");
        _worker = RunAsync(_stop.Token);
    }

    public async Task SendRawAsync(string hex)
    {
        var payload = ParseHex(hex);
        if (payload.Length is 0 or > 20)
            throw new InvalidOperationException("La commande brute doit contenir entre 1 et 20 octets.");
        await WritePacketAsync(payload);
    }

    public async Task ChangeHostAsync(string hostHex)
    {
        var host = ParseHex(hostHex);
        if (host.Length is 0 or > 16)
            throw new InvalidOperationException("L'hôte doit contenir entre 1 et 16 octets en hexadécimal.");

        KeyValuePair<byte, byte>[] devices;
        lock (_deviceFeatures)
            devices = _deviceFeatures.ToArray();
        if (devices.Length == 0)
            throw new InvalidOperationException("Aucun périphérique Logitech appairé n'a encore été détecté.");

        foreach (var device in devices)
        {
            var packet = new byte[20];
            packet[0] = 0x11;
            packet[1] = device.Key;
            packet[2] = device.Value;
            packet[3] = 0x1E;
            host.CopyTo(packet, 4);
            await WritePacketAsync(packet);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        FileStream? stream;
        lock (_streamLock)
            stream = _stream;
        stream?.Dispose();

        if (_worker is not null)
        {
            try
            {
                await _worker;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _stop.Dispose();
        _writeLock.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var handle = LogitechHidNative.Open(_devicePath);
                var stream = new FileStream(handle, FileAccess.ReadWrite, 20, isAsync: true);
                lock (_streamLock)
                    _stream = stream;
                _logCallback("Récepteur Logitech connecté.");

                for (byte deviceId = 1; deviceId <= 6; deviceId++)
                {
                    await WritePacketAsync([0x11, deviceId, 0x00, 0x0D, 0x18, 0x14]);
                    await WritePacketAsync([0x11, deviceId, 0x00, 0x0E, 0x1B, 0x04]);
                    await WritePacketAsync([0x11, deviceId, 0x00, 0x0B, 0x10, 0x04]);
                    await WritePacketAsync([0x11, deviceId, 0x00, 0x0C, 0x10, 0x00]);
                }

                var report = new byte[20];
                while (!cancellationToken.IsCancellationRequested)
                {
                    var bytesRead = await stream.ReadAsync(report, cancellationToken);
                    if (bytesRead == 0)
                        throw new IOException("Le récepteur a fermé la connexion HID.");
                    if (bytesRead >= 8)
                        await ProcessReportAsync(report, cancellationToken);
                    Array.Clear(report);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                               Win32Exception or InvalidOperationException or ObjectDisposedException)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    _logCallback($"Récepteur Logitech indisponible : {exception.Message} Nouvelle tentative dans 2 s.");
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
            }
            finally
            {
                lock (_streamLock)
                    _stream = null;
            }
        }
    }

    private async Task ProcessReportAsync(byte[] report, CancellationToken cancellationToken)
    {
        if (report[0] != 0x11)
            return;

        var deviceId = report[1];
        var feature = report[2];
        var subFeature = report[3] >> 4;
        var function = report[3] & 0x0F;
        var value = report[4];

        if (feature == 0 && subFeature == 0)
        {
            if (function == 0x0D)
            {
                lock (_deviceFeatures)
                    _deviceFeatures[deviceId] = value;
            }
            else if (function == 0x0E)
            {
                lock (_keyFeatures)
                    _keyFeatures[deviceId] = value;
                await WritePacketAsync([0x11, deviceId, value, 0x0E], cancellationToken);
            }
            else if (function is 0x0B or 0x0C && value != 0)
            {
                lock (_batteryFeatures)
                    _batteryFeatures[deviceId] = value;
                var response = function == 0x0B
                    ? new byte[] { 0x11, deviceId, value, 0x10 }
                    : new byte[] { 0x11, deviceId, value };
                await WritePacketAsync(response, cancellationToken);
            }
            return;
        }

        if (feature == 0x04)
        {
            RaiseEvent("WIRELESS_DEVICE_STATUS", Convert.ToHexString(report));
            await RequestFeaturesAsync(deviceId, cancellationToken);
            return;
        }

        if (subFeature == 0 && TryGetFeature(_deviceFeatures, deviceId, out var deviceFeature) &&
            feature == deviceFeature)
        {
            RaiseEvent("Change_Host", Convert.ToHexString(report));
            return;
        }

        if (TryGetFeature(_keyFeatures, deviceId, out var keyFeature) && feature == keyFeature)
        {
            if (subFeature == 0 && function == 0)
            {
                var keys = new List<string>();
                for (var offset = 4; offset + 1 < report.Length; offset += 2)
                {
                    var key = BinaryPrimitives.ReadUInt16BigEndian(report.AsSpan(offset, 2));
                    if (key == 0)
                        continue;
                    keys.Add(KeyNames.TryGetValue(key, out var name) ? name : $"0x{key:X}");
                }
                if (keys.Count > 0)
                    RaiseEvent(string.Join("+", keys), null);
            }
            else if (subFeature == 0 && function != 0 && value != 0x09)
            {
                await WritePacketAsync([0x11, deviceId, feature, 0x3E, 0x00, 0xD1, 0x03], cancellationToken);
                await WritePacketAsync([0x11, deviceId, feature, 0x3E, 0x00, 0xD2, 0x03], cancellationToken);
            }
            return;
        }

        if (TryGetFeature(_batteryFeatures, deviceId, out var batteryFeature) && feature == batteryFeature)
        {
            var state = BatteryStates.TryGetValue(report[6], out var status) ? status : "unknown";
            RaiseEvent("Battery", $"{value}% {state} DeviceId={deviceId:X2}");
        }
    }

    private async Task RequestFeaturesAsync(byte deviceId, CancellationToken cancellationToken)
    {
        await WritePacketAsync([0x11, deviceId, 0x00, 0x0D, 0x18, 0x14], cancellationToken);
        await WritePacketAsync([0x11, deviceId, 0x00, 0x0E, 0x1B, 0x04], cancellationToken);
        await WritePacketAsync([0x11, deviceId, 0x00, 0x0B, 0x10, 0x04], cancellationToken);
        await WritePacketAsync([0x11, deviceId, 0x00, 0x0C, 0x10, 0x00], cancellationToken);
    }

    private void RaiseEvent(string name, string? payload) =>
        _eventCallback($"{_eventPrefix}.{name}", payload);

    private static bool TryGetFeature(Dictionary<byte, byte> features, byte deviceId, out byte feature)
    {
        lock (features)
            return features.TryGetValue(deviceId, out feature);
    }

    private async Task WritePacketAsync(byte[] packet, CancellationToken cancellationToken = default)
    {
        if (packet.Length > 20)
            throw new InvalidOperationException("Un rapport HID Logitech ne peut pas dépasser 20 octets.");
        var report = new byte[20];
        packet.CopyTo(report, 0);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            FileStream? stream;
            lock (_streamLock)
                stream = _stream;
            if (stream is null)
                throw new InvalidOperationException("Le récepteur Logitech n'est pas connecté.");
            await stream.WriteAsync(report, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static byte[] ParseHex(string value)
    {
        var compact = new string(value.Where(character => !char.IsWhiteSpace(character)).ToArray());
        if (compact.Length == 0 || compact.Length % 2 != 0 || compact.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException("Saisissez une suite d'octets hexadécimaux, par exemple 11 01 00 0D.");
        return Convert.FromHexString(compact);
    }
}

internal static class LogitechHidNative
{
    private const uint DevicePresent = 0x00000002;
    private const uint DeviceInterface = 0x00000010;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint Overlapped = 0x40000000;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    public static IReadOnlyList<LogitechReceiver> FindReceivers()
    {
        HidD_GetHidGuid(out var hidGuid);
        var info = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DevicePresent | DeviceInterface);
        if (info == InvalidHandleValue)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Impossible d'énumérer les interfaces HID.");

        try
        {
            var receivers = new List<LogitechReceiver>();
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
                    var path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4)) ?? string.Empty;
                    if (!IsSupportedReceiver(path))
                        continue;

                    var product = path.Contains("pid_c52b", StringComparison.OrdinalIgnoreCase) ? "Unifying" : "Bolt";
                    receivers.Add(new LogitechReceiver(path, $"Logitech {product} — {path}"));
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
            return receivers;
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(info);
        }
    }

    public static SafeFileHandle Open(string path)
    {
        var handle = CreateFile(path, GenericRead | GenericWrite, 3, IntPtr.Zero, OpenExisting, Overlapped, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, "Impossible d'ouvrir le récepteur Logitech.");
        }
        return handle;
    }

    private static bool IsSupportedReceiver(string path)
    {
        var lower = path.ToLowerInvariant();
        return (lower.Contains("vid_046d&pid_c52b") || lower.Contains("vid_046d&pid_c548")) &&
               lower.Contains("&mi_02&col02");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterfaceData
    {
        public int Size;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
}
