using System.Diagnostics;
using System.IO;
using System.Text.Json;
using EventGhostDotNet.PluginContracts;
using EventGhostDotNet.Plugins.Shared;
using System.Windows;

namespace EventGhostDotNet.Plugins.ProcessWatcher;

public sealed class ProcessWatcherModule : IPlugin
{
    private const string ConfigurationId = "Process";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly StartupTaskGroup _tasks = new();

    private sealed record ProcessSettings(string ProcessName, string EventPrefix);

    public string Id => "ProcessWatcher";
    public string Name => "Surveillance de processus";
    public IReadOnlyList<PluginActionDescriptor> Actions { get; } = [];
    public bool HasStartupConfiguration => true;

    public PluginStartupData CreateDefaultStartupConfiguration() =>
        new(ConfigurationId, Guid.NewGuid().ToString(),
            JsonSerializer.Serialize(new ProcessSettings("notepad", "Process.Notepad")), string.Empty);

    public PluginStartupData? ConfigureStartup(PluginStartupData? existing, IPluginHost host)
    {
        ProcessSettings settings;
        try
        {
            settings = existing is null
                ? new ProcessSettings("notepad", "Process.Notepad")
                : ReadSettings(existing.Value);
        }
        catch (JsonException exception)
        {
            MessageBox.Show(host.OwnerWindow(), $"La configuration est invalide.\n{exception.Message}",
                Name, MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
        var values = StartupConfigurationDialog.Show(host.OwnerWindow(), Name,
            [
                new("Nom du processus :", settings.ProcessName),
                new("Préfixe d'événement :", settings.EventPrefix)
            ],
            ValidateSettings);
        return values is null
            ? null
            : new PluginStartupData(ConfigurationId, existing?.InstanceId ?? Guid.NewGuid().ToString(),
                JsonSerializer.Serialize(new ProcessSettings(NormalizeProcessName(values[0]), values[1])), string.Empty);
    }

    public string DescribeStartup(PluginStartupData configuration)
    {
        try
        {
            return $"{Name} — {ReadSettings(configuration.Value).ProcessName}";
        }
        catch (JsonException)
        {
            return $"{Name} — configuration invalide";
        }
    }

    public Task StartupAsync(
        PluginStartupData configuration, IPluginHost host, CancellationToken cancellationToken)
    {
        if (configuration.ConfigurationId != ConfigurationId)
            throw new InvalidOperationException($"Configuration de démarrage inconnue : {configuration.ConfigurationId}");
        var settings = ReadSettings(configuration.Value);
        var wasRunning = IsRunning(settings.ProcessName);

        _tasks.Start(token => WatchAsync(settings, wasRunning, host, token),
            cancellationToken, host, DescribeStartup(configuration));
        host.Log($"{Name} activée pour {settings.ProcessName}.");
        return Task.CompletedTask;
    }

    public PluginActionData? ConfigureAction(string actionId, PluginActionData? existing, IPluginHost host) =>
        throw new InvalidOperationException($"{Name} ne fournit pas d'actions de macro.");

    public string DescribeAction(PluginActionData action) =>
        throw new InvalidOperationException($"{Name} ne fournit pas d'actions de macro.");

    public Task ExecuteActionAsync(PluginActionData action, IPluginHost host, CancellationToken cancellationToken) =>
        throw new InvalidOperationException($"{Name} ne fournit pas d'actions de macro.");

    public Task ResetAsync() => _tasks.StopAsync();

    public ValueTask DisposeAsync() => new(ResetAsync());

    private static async Task WatchAsync(
        ProcessSettings settings,
        bool wasRunning,
        IPluginHost host,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var isRunning = IsRunning(settings.ProcessName);
            if (wasRunning == isRunning)
                continue;

            wasRunning = isRunning;
            var state = isRunning ? "Started" : "Stopped";
            host.RaiseEvent($"{settings.EventPrefix}.{state}", settings.ProcessName);
        }
    }

    private static bool IsRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    private static ProcessSettings ReadSettings(string? value)
    {
        var settings = JsonSerializer.Deserialize<ProcessSettings>(value ?? string.Empty, JsonOptions)
            ?? throw new JsonException("La configuration de surveillance de processus est vide.");
        var error = ValidateSettings([settings.ProcessName, settings.EventPrefix]);
        if (error is not null)
            throw new JsonException(error);
        return settings with { ProcessName = NormalizeProcessName(settings.ProcessName) };
    }

    private static string? ValidateSettings(string[] values)
    {
        if (values.Length != 2 || string.IsNullOrWhiteSpace(values[0]) ||
            values[0].IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            values[0].Contains(Path.DirectorySeparatorChar) || values[0].Contains(Path.AltDirectorySeparatorChar) ||
            string.IsNullOrWhiteSpace(NormalizeProcessName(values[0])))
            return "Indiquez un nom de processus valide, sans chemin.";
        if (string.IsNullOrWhiteSpace(values[1]) || values[1].Any(char.IsWhiteSpace))
            return "Le préfixe d'événement ne doit pas être vide ni contenir d'espace.";
        return null;
    }

    private static string NormalizeProcessName(string processName) =>
        processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;
}
