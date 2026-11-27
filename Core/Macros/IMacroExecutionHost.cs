using EventGhostDotNet.PluginContracts;

namespace EventGhostDotNet.Core.Macros;

public enum MacroLogCategory
{
    Macro,
    Action
}

/// <summary>
/// Effets de l'application dont dépend l'exécution d'une macro : journal, dialogues, notifications,
/// PowerShell, plugins et actions système. Le moteur reste ainsi vérifiable sans interface.
/// </summary>
public interface IMacroExecutionHost
{
    IReadOnlyList<MacroGroup> Groups { get; }
    IReadOnlyList<MacroDefinition> Macros { get; }
    IReadOnlyList<MacroAction> StartupActions { get; }
    IReadOnlyList<PluginStartupConfiguration> PluginStartups { get; }

    void Log(string message, LogLevel level, MacroLogCategory category, int indentLevel);

    string ExpandTemplate(string template, object? previousResult, TriggerEvent? triggerEvent);

    MacroExecutionContext CreateContext(
        CancellationToken sessionToken, object? initialResult, TriggerEvent? triggerEvent, int? logIndentLevel);

    void ShowMessage(string title, string message);

    bool AskYesNo(string title, string message);

    void ShowNotification(string title, string message, string? key);

    void Beep();

    void OpenUrl(Uri uri);

    Task RunPowerShellAsync(MacroAction action, MacroExecutionContext context, CancellationToken cancellationToken);

    Task RunPluginActionAsync(MacroAction action, MacroExecutionContext context, CancellationToken cancellationToken);

    /// <summary>Met à jour la référence de configuration non modifiée après une action système.</summary>
    void ApplySystemActionResult(SystemActionResult result);

    /// <summary>Un démarrage de plugin a changé d'état : la session doit être redémarrée.</summary>
    void OnPluginStartupStateChanged();
}
