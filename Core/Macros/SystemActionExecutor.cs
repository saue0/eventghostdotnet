namespace EventGhostDotNet.Core.Macros;

public sealed record SystemActionResult(
    string Message, ActivationTargetKind Kind, string Target, string Value, bool PreviousState, bool CurrentState);

/// <summary>Actions système qui activent ou désactivent un élément de la configuration.</summary>
public static class SystemActionExecutor
{
    public static bool IsSystemAction(MacroActionKind kind) =>
        kind is MacroActionKind.EnableMacro or MacroActionKind.DisableMacro or
            MacroActionKind.EnableAction or MacroActionKind.DisableAction or
            MacroActionKind.ActivateItem or MacroActionKind.DeactivateItem or
            MacroActionKind.Jump or MacroActionKind.If or MacroActionKind.IfJump;

    public static bool IsTargetedSystemAction(MacroActionKind kind) =>
        kind is MacroActionKind.EnableMacro or MacroActionKind.DisableMacro or
            MacroActionKind.EnableAction or MacroActionKind.DisableAction or
            MacroActionKind.ActivateItem or MacroActionKind.DeactivateItem;

    public static string SystemActionName(MacroActionKind kind) => kind switch
    {
        MacroActionKind.EnableMacro => "Système — activer une macro",
        MacroActionKind.DisableMacro => "Système — désactiver une macro",
        MacroActionKind.EnableAction => "Système — activer une action",
        MacroActionKind.DisableAction => "Système — désactiver une action",
        MacroActionKind.ActivateItem => "Système — activer",
        MacroActionKind.DeactivateItem => "Système — désactiver",
        MacroActionKind.Jump => "Système — Jump",
        MacroActionKind.If => "Système — If",
        MacroActionKind.IfJump => "Système — If / Jump / Else Jump",
        _ => throw new InvalidOperationException($"Action système inconnue : {kind}")
    };

    public static string ActionTargetName(MacroAction action) =>
        IsSystemAction(action.Kind) ? SystemActionName(action.Kind) : action.DisplayName;

    public static ActivationTargetKind GetActivationTargetKind(MacroAction action) => action.Kind switch
    {
        MacroActionKind.EnableMacro or MacroActionKind.DisableMacro => ActivationTargetKind.Macro,
        MacroActionKind.EnableAction or MacroActionKind.DisableAction => ActivationTargetKind.Action,
        MacroActionKind.ActivateItem or MacroActionKind.DeactivateItem => action.ActivationTarget
            ?? throw new InvalidOperationException("Le type de l'élément cible est absent."),
        _ => throw new InvalidOperationException($"Action système inconnue : {action.Kind}")
    };

    public static ConfigurationItem? FindActivationTarget(
        IEnumerable<MacroGroup> groups, IEnumerable<MacroDefinition> macros,
        IEnumerable<MacroAction> startupActions, IEnumerable<PluginStartupConfiguration> startups,
        ActivationTargetKind kind, string target, string value)
    {
        switch (kind)
        {
            case ActivationTargetKind.Group:
                return Guid.TryParse(target, out var groupId)
                    ? groups.SingleOrDefault(group => group.Id == groupId) : null;
            case ActivationTargetKind.Macro:
                return Guid.TryParse(target, out var macroId)
                    ? macros.SingleOrDefault(macro => macro.Id == macroId) : null;
            case ActivationTargetKind.Event:
                if (!Guid.TryParse(target, out var eventMacroId))
                    return null;
                var eventMacro = macros.SingleOrDefault(macro => macro.Id == eventMacroId);
                return eventMacro?.EventItems.SingleOrDefault(item =>
                    string.Equals(item.Name, value, StringComparison.OrdinalIgnoreCase));
            case ActivationTargetKind.Action:
                if (!Guid.TryParse(value, out var actionId))
                    return null;
                var actions = string.Equals(target, "Startup", StringComparison.OrdinalIgnoreCase)
                    ? startupActions
                    : Guid.TryParse(target, out var actionMacroId)
                        ? macros.SingleOrDefault(macro => macro.Id == actionMacroId)?.Actions
                        : null;
                return actions?.SingleOrDefault(item => item.Id == actionId);
            case ActivationTargetKind.PluginStartup:
                return startups.SingleOrDefault(item => string.Equals(item.InstanceId, target, StringComparison.Ordinal));
            default:
                throw new InvalidOperationException($"Type d'élément cible inconnu : {kind}");
        }
    }

    public static bool GetActivationState(ConfigurationItem item) => item switch
    {
        MacroGroup group => group.IsEnabled,
        MacroDefinition macro => macro.IsEnabled,
        MacroEvent macroEvent => macroEvent.IsEnabled,
        MacroAction action => action.IsEnabled,
        PluginStartupConfiguration startup => startup.IsEnabled,
        _ => throw new InvalidOperationException("Cet élément ne peut pas être activé ou désactivé.")
    };

    public static void SetActivationState(ConfigurationItem item, bool enabled)
    {
        switch (item)
        {
            case MacroGroup group: group.IsEnabled = enabled; break;
            case MacroDefinition macro: macro.IsEnabled = enabled; break;
            case MacroEvent macroEvent: macroEvent.IsEnabled = enabled; break;
            case MacroAction action: action.IsEnabled = enabled; break;
            case PluginStartupConfiguration startup: startup.IsEnabled = enabled; break;
            default: throw new InvalidOperationException("Cet élément ne peut pas être activé ou désactivé.");
        }
    }

    public static string ActivationTargetName(ConfigurationItem item) => item switch
    {
        MacroGroup group => $"Groupe « {group.Name} »",
        MacroDefinition macro => $"Macro « {macro.Name} »",
        MacroEvent macroEvent => $"Événement « {macroEvent.Name} » de « {macroEvent.Macro.Name} »",
        MacroAction action => $"Action « {ActionTargetName(action)} »",
        PluginStartupConfiguration startup => $"Démarrage « {startup.DisplayName} »",
        _ => throw new InvalidOperationException("Cet élément ne peut pas être activé ou désactivé.")
    };

    public static SystemActionResult Execute(
        IEnumerable<MacroGroup> groups, IEnumerable<MacroDefinition> macros,
        IEnumerable<MacroAction> startupActions, IEnumerable<PluginStartupConfiguration> startups, MacroAction action)
    {
        var kind = GetActivationTargetKind(action);
        var target = FindActivationTarget(groups, macros, startupActions, startups, kind, action.Target, action.Value)
            ?? throw new InvalidOperationException("L'élément cible est introuvable.");
        var previous = GetActivationState(target);
        var enabled = action.Kind is MacroActionKind.EnableMacro or MacroActionKind.EnableAction or MacroActionKind.ActivateItem;
        SetActivationState(target, enabled);
        return new SystemActionResult($"{ActivationTargetName(target)} : {(enabled ? "activé" : "désactivé")}.",
            kind, action.Target, action.Value, previous, enabled);
    }
}
