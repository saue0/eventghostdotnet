using System.Globalization;
using System.Text.Json;
using EventGhostDotNet.PluginContracts;
using EventGhostDotNet.Plugins.Shared;
using System.Windows;

namespace EventGhostDotNet.Plugins.Scheduler;

public sealed class SchedulerModule : IPlugin
{
    private const string ConfigurationId = "DailySchedule";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly StartupTaskGroup _tasks = new();

    private sealed record ScheduleSettings(string Time, string EventName);

    public string Id => "Scheduler";
    public string Name => "Planificateur";
    public IReadOnlyList<PluginActionDescriptor> Actions { get; } = [];
    public bool HasStartupConfiguration => true;

    public PluginStartupData CreateDefaultStartupConfiguration() =>
        new(ConfigurationId, Guid.NewGuid().ToString(),
            JsonSerializer.Serialize(new ScheduleSettings("08:00", "Schedule.Morning")), string.Empty);

    public PluginStartupData? ConfigureStartup(PluginStartupData? existing, IPluginHost host)
    {
        ScheduleSettings settings;
        try
        {
            settings = existing is null
                ? new ScheduleSettings("08:00", "Schedule.Morning")
                : ReadSettings(existing.Value);
        }
        catch (JsonException exception)
        {
            MessageBox.Show(host.Owner, $"La configuration est invalide.\n{exception.Message}",
                Name, MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
        var values = StartupConfigurationDialog.Show(host.Owner, "Planificateur",
            [
                new("Heure quotidienne (HH:mm) :", settings.Time),
                new("Nom de l'événement :", settings.EventName)
            ],
            ValidateSettings);
        return values is null
            ? null
            : new PluginStartupData(ConfigurationId, existing?.InstanceId ?? Guid.NewGuid().ToString(),
                JsonSerializer.Serialize(new ScheduleSettings(values[0], values[1])), string.Empty);
    }

    public string DescribeStartup(PluginStartupData configuration)
    {
        try
        {
            var settings = ReadSettings(configuration.Value);
            return $"{Name} — {settings.Time} → {settings.EventName}";
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
        var time = TimeOnly.ParseExact(settings.Time, "HH:mm", CultureInfo.InvariantCulture);
        var current = DateTime.Now;
        var lastCheckedMinute = new DateTime(current.Year, current.Month, current.Day, current.Hour, current.Minute, 0);

        _tasks.Start(token => RunAsync(settings.EventName, time, lastCheckedMinute, host, token),
            cancellationToken, host, DescribeStartup(configuration));
        host.Log($"{Name} activé : événement {settings.EventName} chaque jour à {settings.Time}.");
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

    private static async Task RunAsync(
        string eventName,
        TimeOnly time,
        DateTime lastCheckedMinute,
        IPluginHost host,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var now = DateTime.Now;
            if (now.Hour != time.Hour || now.Minute != time.Minute)
                continue;

            var scheduledMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
            if (scheduledMinute == lastCheckedMinute)
                continue;

            lastCheckedMinute = scheduledMinute;
            host.RaiseEvent(eventName, now.ToString("O", CultureInfo.InvariantCulture));
        }
    }

    private static ScheduleSettings ReadSettings(string? value)
    {
        var settings = JsonSerializer.Deserialize<ScheduleSettings>(value ?? string.Empty, JsonOptions)
            ?? throw new JsonException("La configuration du planificateur est vide.");
        var error = ValidateSettings([settings.Time, settings.EventName]);
        if (error is not null)
            throw new JsonException(error);
        return settings;
    }

    private static string? ValidateSettings(string[] values)
    {
        if (values.Length != 2 || !TimeOnly.TryParseExact(
                values[0], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return "L'heure doit respecter le format 24 heures HH:mm, par exemple 07:30.";
        if (string.IsNullOrWhiteSpace(values[1]) || values[1].Any(char.IsWhiteSpace))
            return "Le nom de l'événement ne doit pas être vide ni contenir d'espace.";
        return null;
    }
}
