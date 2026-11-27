using EventGhostDotNet.PluginContracts;

namespace EventGhostDotNet.Core.Macros;

/// <summary>
/// Contexte d'une action en cours : résultat transmis à l'action suivante, journal et émission
/// d'événements. L'application fournit les deux délégués, qui marshallent vers son interface.
/// </summary>
public sealed class MacroExecutionContext : IActionContext
{
    private readonly Action<string, LogLevel, int?> _log;
    private readonly Action<string, string?, string?> _raiseEvent;
    private readonly object _resultLock = new();
    private object? _result;

    public MacroExecutionContext(
        CancellationToken sessionToken,
        object? initialResult,
        TriggerEvent? triggerEvent,
        int? logIndentLevel,
        Action<string, LogLevel, int?> log,
        Action<string, string?, string?> raiseEvent)
    {
        CancellationToken = sessionToken;
        _result = initialResult;
        Event = triggerEvent;
        LogIndentLevel = logIndentLevel;
        _log = log;
        _raiseEvent = raiseEvent;
    }

    public TriggerEvent? Event { get; }
    public CancellationToken CancellationToken { get; }
    public int? LogIndentLevel { get; }

    public object? Result
    {
        get
        {
            lock (_resultLock)
                return _result;
        }
        set
        {
            lock (_resultLock)
                _result = value;
        }
    }

    public void Log(string message) => Log(message, LogLevel.Information);

    public void Log(string message, LogLevel level)
    {
        if (!Enum.IsDefined(level))
            throw new InvalidOperationException($"Niveau de journal invalide : {level}.");
        _log(message, level, LogIndentLevel);
    }

    public void RaiseEvent(string name, string? payload) => RaiseEventFromSource(name, payload, null);

    public void RaiseEventFromSource(string name, string? payload, string? sourceId) =>
        _raiseEvent(name, payload, sourceId);
}
