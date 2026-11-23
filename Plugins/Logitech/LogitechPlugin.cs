using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using EventGhostDotNet.PluginContracts;

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
    private readonly IHidDeviceService _hidDevices;
    private readonly ISessionState? _session;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _streamLock = new();
    private readonly Dictionary<byte, byte> _deviceFeatures = [];
    private readonly Dictionary<byte, byte> _keyFeatures = [];
    private readonly Dictionary<byte, byte> _batteryFeatures = [];
    private FileStream? _stream;
    private Task? _worker;
    private TaskCompletionSource _deviceChange = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public LogitechUnifyClient(
        LogitechReceiver receiver,
        string eventPrefix,
        Action<string, string?> eventCallback,
        Action<string> logCallback,
        IHidDeviceService hidDevices,
        ISessionState? session = null)
    {
        _devicePath = receiver.Path;
        _eventPrefix = string.IsNullOrWhiteSpace(eventPrefix) ? "Logitech" : eventPrefix.Trim();
        _eventCallback = eventCallback;
        _logCallback = logCallback;
        _hidDevices = hidDevices;
        _session = session;
        _hidDevices.DevicesChanged += OnDevicesChanged;
        if (session is not null)
            session.StatusChanged += OnSessionStatusChanged;
    }

    public static IReadOnlyList<LogitechReceiver> FindReceivers(IHidDeviceService? hidDevices)
    {
        if (hidDevices is null)
            throw new NotSupportedException("Les périphériques HID ne sont pas disponibles sur cette plateforme.");
        return hidDevices.EnumerateDevicePaths()
            .Where(IsSupportedReceiver)
            .Select(path => new LogitechReceiver(path,
                $"Logitech {(path.Contains("pid_c52b", StringComparison.OrdinalIgnoreCase) ? "Unifying" : "Bolt")} — {path}"))
            .ToArray();
    }

    private static bool IsSupportedReceiver(string path)
    {
        var lower = path.ToLowerInvariant();
        return (lower.Contains("vid_046d&pid_c52b") || lower.Contains("vid_046d&pid_c548")) &&
               lower.Contains("&mi_02&col02");
    }

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

        _hidDevices.DevicesChanged -= OnDevicesChanged;
        if (_session is not null)
            _session.StatusChanged -= OnSessionStatusChanged;
        _stop.Dispose();
        _writeLock.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await WaitForActiveAsync(cancellationToken);
            try
            {
                using var handle = _hidDevices.Open(_devicePath);
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
                    try
                    {
                        await WaitBeforeRetryAsync(
                            $"Récepteur Logitech indisponible : {exception.Message} Nouvelle tentative automatique.",
                            cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
            finally
            {
                lock (_streamLock)
                    _stream = null;
            }
        }
    }

    private void OnDevicesChanged() => Interlocked.Exchange(ref _deviceChange,
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
        .TrySetResult();

    private void OnSessionStatusChanged(SessionStatus status)
    {
        if (status == SessionStatus.Active)
            return;

        FileStream? stream;
        lock (_streamLock)
        {
            stream = _stream;
            _stream = null;
        }
        stream?.Dispose();
    }

    private async Task WaitForActiveAsync(CancellationToken cancellationToken)
    {
        while (_session is { Status: not SessionStatus.Active })
            await Task.Delay(500, cancellationToken);
    }

    private async Task WaitBeforeRetryAsync(string message, CancellationToken cancellationToken)
    {
        if (_session is { Status: not SessionStatus.Active })
        {
            await WaitForActiveAsync(cancellationToken);
            return;
        }

        _logCallback(message);

        // Réessai après 2 s, ou immédiatement dès qu'une interface HID change.
        await Task.WhenAny(
            Task.Delay(TimeSpan.FromSeconds(2), cancellationToken),
            Volatile.Read(ref _deviceChange).Task);
        cancellationToken.ThrowIfCancellationRequested();
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
