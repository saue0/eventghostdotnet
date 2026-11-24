using System.Globalization;
using System.Text.Json;
using EventGhostDotNet.PluginContracts;
using EventGhostDotNet.Plugins.Shared;

namespace EventGhostDotNet.Plugins.Scheduler;

public sealed class SchedulerModule : IPlugin
{
    private const string ConfigurationId = "DailySchedule";
    private const int MaximumSeconds = 2_000_000;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly StartupTaskGroup _schedules = new();
    private readonly object _jobLock = new();
    private readonly Dictionary<string, List<Job>> _jobs = new(StringComparer.OrdinalIgnoreCase);

    internal sealed record Schedule(string Kind, string Time, string Days, string Date, int IdleSeconds, string EventName);
    internal sealed record ScheduleList(List<Schedule> Schedules);
    private sealed record LegacySchedule(string Time, string EventName);
    private sealed record ActionSettings(string Id, int Seconds, string EventName, string Payload, Guid MacroId);

    private sealed class Job(CancellationTokenSource cancellation)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public string Id => "Scheduler";
    public string Name => "Planificateur";
    public bool HasStartupConfiguration => true;
    public IReadOnlyList<PluginActionDescriptor> Actions { get; } =
    [
        new("DelayedEvent", "Événement différé"),
        new("DelayedMacro", "Macro différée"),
        new("Cancel", "Annuler une tâche planifiée"),
        new("Repeat", "Minuterie répétitive"),
        new("Countdown", "Compte à rebours")
    ];

    public PluginStartupData CreateDefaultStartupConfiguration() =>
        new(ConfigurationId, Guid.NewGuid().ToString(), JsonSerializer.Serialize(new ScheduleList([])), string.Empty);

    public PluginStartupData? ConfigureStartup(PluginStartupData? existing, IPluginHost host)
    {
        var settings = existing is null ? new ScheduleList([]) : ReadSchedules(existing.Value);
        var updated = SchedulerDialogs.ConfigureSchedules(host.OwnerWindow(), settings.Schedules);
        return updated is null ? null : new PluginStartupData(ConfigurationId,
            existing?.InstanceId ?? Guid.NewGuid().ToString(),
            JsonSerializer.Serialize(new ScheduleList(updated)), string.Empty);
    }

    public string DescribeStartup(PluginStartupData configuration)
    {
        try
        {
            var count = ReadSchedules(configuration.Value).Schedules.Count;
            return $"{Name} — {count} planification(s)";
        }
        catch (JsonException)
        {
            return $"{Name} — configuration invalide";
        }
    }

    public Task StartupAsync(PluginStartupData configuration, IPluginHost host, CancellationToken cancellationToken)
    {
        if (configuration.ConfigurationId != ConfigurationId)
            throw new InvalidOperationException($"Configuration de démarrage inconnue : {configuration.ConfigurationId}");
        var settings = ReadSchedules(configuration.Value);
        foreach (var schedule in settings.Schedules)
            _schedules.Start(token => RunScheduleAsync(schedule, host, token),
                cancellationToken, host, DescribeSchedule(schedule));
        host.Log($"{Name} activé : {settings.Schedules.Count} planification(s).");
        return Task.CompletedTask;
    }

    public PluginActionData? ConfigureAction(string actionId, PluginActionData? existing, IPluginHost host)
    {
        EnsureActionId(actionId);
        var settings = existing is null
            ? new ActionSettings("minuterie", 60, "Schedule.Timer", string.Empty, Guid.Empty)
            : ReadAction(existing);
        var values = SchedulerDialogs.ConfigureAction(host, actionId, settings.Id, settings.Seconds,
            settings.EventName, settings.Payload, settings.MacroId, ValidateActionFields);
        if (values is null)
            return null;
        var updated = new ActionSettings(values.Id, values.Seconds, values.EventName, values.Payload, values.MacroId);
        return new PluginActionData(actionId, JsonSerializer.Serialize(updated), string.Empty);
    }

    public string DescribeAction(PluginActionData action)
    {
        try
        {
            var settings = ReadAction(action);
            return action.ActionId switch
            {
                "Cancel" => $"Annuler la tâche « {settings.Id} »",
                "DelayedMacro" => $"Macro différée — {settings.Id} ({settings.Seconds} s)",
                "Repeat" => $"Minuterie répétitive — {settings.Id} ({settings.Seconds} s)",
                "Countdown" => $"Compte à rebours — {settings.Id} ({settings.Seconds} s)",
                _ => $"Événement différé — {settings.Id} ({settings.Seconds} s)"
            };
        }
        catch (JsonException)
        {
            return "Planificateur — action invalide";
        }
    }

    public Task ExecuteActionAsync(PluginActionData action, IPluginHost host, CancellationToken cancellationToken)
    {
        var settings = ReadAction(action);
        cancellationToken.ThrowIfCancellationRequested();
        if (action.ActionId == "Cancel")
        {
            Job[] jobs;
            lock (_jobLock)
            {
                if (!_jobs.Remove(settings.Id, out var entries))
                    throw new InvalidOperationException($"Tâche planifiée introuvable : {settings.Id}.");
                jobs = entries.ToArray();
            }
            foreach (var job in jobs)
                job.Cancellation.Cancel();
            host.Log(jobs.Length == 1
                ? $"Annulation demandée pour la tâche « {settings.Id} »."
                : $"Annulation demandée pour {jobs.Length} tâches « {settings.Id} ».");
            return Task.CompletedTask;
        }

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var entry = new Job(cancellation);
        int parallelCount;
        lock (_jobLock)
        {
            if (!_jobs.TryGetValue(settings.Id, out var entries))
                _jobs[settings.Id] = entries = [];
            entries.Add(entry);
            parallelCount = entries.Count;
        }
        _ = RunJobAsync(action.ActionId, settings, entry, host);
        host.Log(parallelCount == 1
            ? $"Tâche « {settings.Id} » planifiée dans {settings.Seconds} seconde(s)."
            : $"Tâche « {settings.Id} » planifiée dans {settings.Seconds} seconde(s) ({parallelCount} en parallèle).");
        return Task.CompletedTask;
    }

    public async Task ResetAsync()
    {
        Job[] jobs;
        lock (_jobLock)
        {
            jobs = _jobs.Values.SelectMany(entries => entries).ToArray();
            _jobs.Clear();
        }
        foreach (var job in jobs)
            job.Cancellation.Cancel();
        await Task.WhenAll(_schedules.StopAsync(), Task.WhenAll(jobs.Select(job => job.Completion.Task)));
    }

    public ValueTask DisposeAsync() => new(ResetAsync());

    private async Task RunJobAsync(string actionId, ActionSettings settings, Job entry, IPluginHost host)
    {
        try
        {
            do
            {
                await Task.Delay(TimeSpan.FromSeconds(settings.Seconds), entry.Cancellation.Token);
                entry.Cancellation.Token.ThrowIfCancellationRequested();
                if (actionId == "DelayedMacro")
                    await host.RunMacroAsync(settings.MacroId, entry.Cancellation.Token);
                else
                    host.RaiseEvent(settings.EventName, string.IsNullOrEmpty(settings.Payload) ? null : settings.Payload);
            } while (actionId == "Repeat");
        }
        catch (OperationCanceledException) when (entry.Cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            host.Log($"Erreur de la tâche « {settings.Id} » : {exception.Message}", LogLevel.Error);
        }
        finally
        {
            lock (_jobLock)
            {
                if (_jobs.TryGetValue(settings.Id, out var entries))
                {
                    entries.Remove(entry);
                    if (entries.Count == 0)
                        _jobs.Remove(settings.Id);
                }
                entry.Cancellation.Dispose();
            }
            entry.Completion.TrySetResult();
        }
    }

    private static async Task RunScheduleAsync(Schedule schedule, IPluginHost host, CancellationToken cancellationToken)
    {
        var idleProvider = host.IdleTime;
        if (schedule.Kind == "Idle" && idleProvider is null)
        {
            host.Log("Planificateur : l'inactivité système n'est pas disponible sur cette plateforme ; planification ignorée.",
                LogLevel.Warning);
            return;
        }

        var now = DateTime.Now;
        var lastMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
        var idleTriggered = false;
        long? lastInput = null;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            now = DateTime.Now;
            if (schedule.Kind == "Idle")
            {
                var snapshot = idleProvider!.GetIdleSnapshot();
                var seconds = (uint)Math.Clamp(snapshot.IdleTime.TotalSeconds, 0, uint.MaxValue);
                if (ShouldTriggerIdle(ref lastInput, ref idleTriggered, snapshot.InputToken, seconds, schedule.IdleSeconds))
                    host.RaiseEvent(schedule.EventName, now.ToString("O", CultureInfo.InvariantCulture));
                continue;
            }

            var minute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
            if (minute == lastMinute || !MatchesSchedule(schedule, now))
                continue;
            lastMinute = minute;
            host.RaiseEvent(schedule.EventName, now.ToString("O", CultureInfo.InvariantCulture));
        }
    }

    internal static bool MatchesSchedule(Schedule schedule, DateTime now)
    {
        if (now.ToString("HH:mm", CultureInfo.InvariantCulture) != schedule.Time)
            return false;
        return schedule.Kind switch
        {
            "Daily" => true,
            "Weekly" => schedule.Days.Split(',').Contains(((int)now.DayOfWeek).ToString(CultureInfo.InvariantCulture)),
            "Once" => now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) == schedule.Date,
            _ => false
        };
    }

    internal static bool ShouldTriggerIdle(
        ref long? lastInput, ref bool triggered, long input, uint seconds, int threshold)
    {
        if (lastInput != input || seconds < threshold)
            triggered = false;
        lastInput = input;
        if (seconds < threshold || triggered)
            return false;
        triggered = true;
        return true;
    }

    internal static string DescribeSchedule(Schedule schedule) => schedule.Kind switch
    {
        "Daily" => $"Quotidienne {schedule.Time} → {schedule.EventName}",
        "Weekly" => $"Hebdomadaire {schedule.Days} à {schedule.Time} → {schedule.EventName}",
        "Once" => $"Le {schedule.Date} à {schedule.Time} → {schedule.EventName}",
        "Idle" => $"Inactivité {schedule.IdleSeconds} s → {schedule.EventName}",
        _ => throw new JsonException("Type de planification inconnu.")
    };

    private static ScheduleList ReadSchedules(string value)
    {
        using var document = JsonDocument.Parse(value);
        ScheduleList settings;
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("La configuration du planificateur doit être un objet.");
        if (document.RootElement.EnumerateObject().Any(property =>
                string.Equals(property.Name, "Time", StringComparison.OrdinalIgnoreCase)))
        {
            var legacy = JsonSerializer.Deserialize<LegacySchedule>(value, JsonOptions)
                ?? throw new JsonException("Configuration du planificateur vide.");
            settings = new ScheduleList([new Schedule("Daily", legacy.Time, "", "", 0, legacy.EventName)]);
        }
        else
            settings = JsonSerializer.Deserialize<ScheduleList>(value, JsonOptions)
                ?? throw new JsonException("Configuration du planificateur vide.");
        if (settings.Schedules is null)
            throw new JsonException("La liste des planifications est absente.");
        foreach (var schedule in settings.Schedules)
        {
            if (schedule is null)
                throw new JsonException("Une planification est vide.");
            var error = ValidateSchedule(schedule);
            if (error is not null)
                throw new JsonException(error);
        }
        return settings;
    }

    internal static string? ValidateSchedule(Schedule schedule)
    {
        if (schedule.Kind is not ("Daily" or "Weekly" or "Once" or "Idle"))
            return "Type de planification inconnu.";
        if (!ValidEventName(schedule.EventName))
            return "Le nom de l'événement ne doit pas être vide ni contenir d'espace.";
        if (schedule.Kind == "Idle")
            return schedule.IdleSeconds is < 1 or > MaximumSeconds
                ? $"L'inactivité doit être comprise entre 1 et {MaximumSeconds} secondes." : null;
        if (!TimeOnly.TryParseExact(schedule.Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return "L'heure doit respecter le format HH:mm.";
        if (schedule.Kind == "Once" && !DateOnly.TryParseExact(schedule.Date, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return "La date doit respecter le format AAAA-MM-JJ.";
        if (schedule.Kind == "Weekly" && (string.IsNullOrEmpty(schedule.Days) ||
            schedule.Days.Split(',').Any(day => day is not ("0" or "1" or "2" or "3" or "4" or "5" or "6"))))
            return "Les jours doivent être séparés par des virgules : 0 = dimanche, 1 = lundi, …, 6 = samedi.";
        return null;
    }

    private static ActionSettings ReadAction(PluginActionData action)
    {
        EnsureActionId(action.ActionId);
        var settings = JsonSerializer.Deserialize<ActionSettings>(action.Value, JsonOptions)
            ?? throw new JsonException("Configuration de l'action vide.");
        var error = ValidateActionFields(action.ActionId, settings.Id, settings.Seconds,
            settings.EventName, settings.MacroId);
        if (error is not null)
            throw new JsonException(error);
        return settings;
    }

    private static string? ValidateActionFields(string actionId, string id, int seconds, string eventName, Guid macroId)
    {
        if (string.IsNullOrWhiteSpace(id))
            return "L'identifiant de la tâche est obligatoire.";
        if (actionId == "Cancel")
            return null;
        if (seconds is < 1 or > MaximumSeconds)
            return $"La durée doit être comprise entre 1 et {MaximumSeconds} secondes.";
        if (actionId == "DelayedMacro")
            return macroId == Guid.Empty ? "Choisissez une macro." : null;
        return ValidEventName(eventName) ? null : "Le nom de l'événement ne doit pas être vide ni contenir d'espace.";
    }

    private static bool ValidEventName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && !name.Any(char.IsWhiteSpace);

    private static void EnsureActionId(string actionId)
    {
        if (actionId is not ("DelayedEvent" or "DelayedMacro" or "Cancel" or "Repeat" or "Countdown"))
            throw new InvalidOperationException($"Action du planificateur inconnue : {actionId}");
    }
}
