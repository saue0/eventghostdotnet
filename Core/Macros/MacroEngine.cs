using System.ComponentModel;
using System.IO;
using System.Security;
using System.Text.Json;
using EventGhostDotNet.PluginContracts;

namespace EventGhostDotNet.Core.Macros;

/// <summary>
/// Exécute une séquence d'actions de macro. Le moteur ne dépend d'aucune interface : les dialogues,
/// notifications, plugins et PowerShell passent par <see cref="IMacroExecutionHost"/>, ce qui permet
/// de vérifier le flux (branches, sauts, limites, résultats) avec un hôte de test.
/// </summary>
public sealed class MacroEngine(IMacroExecutionHost host)
{
    public const int MaximumLogIndentLevel = 64;

    public async Task ExecuteAsync(
        MacroDefinition macro, CancellationToken cancellationToken, int callDepth = 0, Guid? onlyActionId = null,
        TriggerEvent? triggerEvent = null, int logIndentLevel = 0, CancellationToken? actionContextToken = null)
    {
        if (cancellationToken.IsCancellationRequested)
            return;
        MacroExecutionLimits.EnsureCallDepth(callDepth);

        var selectedAction = onlyActionId is Guid selectedActionId
            ? macro.Actions.FirstOrDefault(action => action.Id == selectedActionId)
            : null;
        if (onlyActionId is not null && selectedAction is null)
            throw new InvalidOperationException("L'action sélectionnée n'appartient plus à la macro.");

        var actionLogIndentLevel = NextLogIndentLevel(logIndentLevel);
        var entryIndentLevel = selectedAction is null ? logIndentLevel : actionLogIndentLevel;
        host.Log(selectedAction is null
                ? $"Exécution de la macro « {macro.Name} »."
                : $"Exécution de l'action « {selectedAction.DisplayName} ».",
            LogLevel.Information,
            selectedAction is null ? MacroLogCategory.Macro : MacroLogCategory.Action,
            entryIndentLevel);
        var actionIds = selectedAction is null
            ? macro.Actions.Select(action => action.Id).ToArray()
            : new[] { selectedAction.Id };
        var actionIndex = 0;
        var actionExecutions = 0;
        object? previousActionResult = null;
        while (actionIndex < actionIds.Length)
        {
            if (cancellationToken.IsCancellationRequested)
                break;
            if (!MacroExecutionLimits.CanExecuteAction(++actionExecutions))
            {
                host.Log(
                    $"Limite de {MacroExecutionLimits.MaximumActionExecutionsPerMacro} exécutions d'actions atteinte dans « {macro.Name} ».",
                    LogLevel.Warning, MacroLogCategory.Macro, logIndentLevel);
                break;
            }

            var actionId = actionIds[actionIndex];
            var action = macro.Actions.FirstOrDefault(item => item.Id == actionId);
            if (action is null || !action.IsEnabled)
            {
                previousActionResult = null;
                actionIndex++;
                continue;
            }

            var nextActionIndex = actionIndex + 1;
            var stopAfterJump = false;
            var actionContext = host.CreateContext(
                actionContextToken ?? cancellationToken,
                action.Kind is MacroActionKind.If or MacroActionKind.IfJump
                    ? previousActionResult
                    : action.Value.Length == 0 ? null : action.Value,
                triggerEvent,
                actionLogIndentLevel);
            try
            {
                switch (action.Kind)
                {
                    case MacroActionKind.Log:
                        var logMessage = host.ExpandTemplate(action.Value, previousActionResult, triggerEvent);
                        actionContext.Result = logMessage;
                        actionContext.Log(logMessage, action.LogLevel ?? LogLevel.Information);
                        break;
                    case MacroActionKind.Delay:
                        await Task.Delay(MacroExecutionLimits.ParseDelay(action.Value), cancellationToken);
                        break;
                    case MacroActionKind.Beep:
                        host.Beep();
                        break;
                    case MacroActionKind.ShowMessage:
                        var message = host.ExpandTemplate(action.Value, previousActionResult, triggerEvent);
                        actionContext.Result = message;
                        host.ShowMessage(macro.Name, message);
                        break;
                    case MacroActionKind.Notify:
                        if (string.IsNullOrWhiteSpace(action.Value))
                            throw new InvalidOperationException("Le texte de la notification est vide.");
                        var notificationMessage = host.ExpandTemplate(action.Value, previousActionResult, triggerEvent);
                        var notificationTitle = string.IsNullOrWhiteSpace(action.Target)
                            ? macro.Name
                            : host.ExpandTemplate(action.Target, previousActionResult, triggerEvent);
                        var notificationKey = string.IsNullOrWhiteSpace(action.NotificationKey)
                            ? null
                            : host.ExpandTemplate(action.NotificationKey, previousActionResult, triggerEvent);
                        host.ShowNotification(notificationTitle, notificationMessage, notificationKey);
                        actionContext.Result = notificationMessage;
                        break;
                    case MacroActionKind.AskYesNo:
                        var question = host.ExpandTemplate(action.Value, previousActionResult, triggerEvent);
                        if (string.IsNullOrWhiteSpace(question))
                            throw new InvalidOperationException("La question est vide.");
                        cancellationToken.ThrowIfCancellationRequested();
                        var questionTitle = string.IsNullOrWhiteSpace(action.Target)
                            ? macro.Name
                            : host.ExpandTemplate(action.Target, previousActionResult, triggerEvent);
                        var answer = host.AskYesNo(questionTitle, question);
                        cancellationToken.ThrowIfCancellationRequested();
                        actionContext.Result = answer ? "Oui" : "Non";
                        break;
                    case MacroActionKind.Stop:
                        nextActionIndex = actionIds.Length;
                        break;
                    case MacroActionKind.OpenUrl:
                        var url = host.ExpandTemplate(action.Value, previousActionResult, triggerEvent);
                        if (!MacroExecutionLimits.IsAllowedUrl(url, out var uri))
                            throw new InvalidOperationException($"URL non autorisée : {url}");
                        actionContext.Result = url;
                        host.OpenUrl(uri!);
                        break;
                    case MacroActionKind.PowerShell:
                        await host.RunPowerShellAsync(action, actionContext, cancellationToken);
                        break;
                    case MacroActionKind.RaiseEvent:
                        if (string.IsNullOrWhiteSpace(action.Target))
                            throw new InvalidOperationException("Le nom de l'événement est vide.");
                        var payload = string.IsNullOrEmpty(action.Value)
                            ? null
                            : host.ExpandTemplate(action.Value, previousActionResult, triggerEvent);
                        actionContext.Result = payload;
                        actionContext.RaiseEvent(action.Target, payload);
                        break;
                    case MacroActionKind.Plugin:
                        await host.RunPluginActionAsync(action, actionContext, cancellationToken);
                        break;
                    case MacroActionKind.EnableMacro:
                    case MacroActionKind.DisableMacro:
                    case MacroActionKind.EnableAction:
                    case MacroActionKind.DisableAction:
                    case MacroActionKind.ActivateItem:
                    case MacroActionKind.DeactivateItem:
                        var result = SystemActionExecutor.Execute(
                            host.Groups, host.Macros, host.StartupActions, host.PluginStartups, action);
                        host.ApplySystemActionResult(result);
                        actionContext.Log(result.Message);
                        if (result.Kind == ActivationTargetKind.PluginStartup &&
                            result.PreviousState != result.CurrentState)
                        {
                            host.OnPluginStartupStateChanged();
                            nextActionIndex = actionIds.Length;
                        }
                        break;
                    case MacroActionKind.Jump:
                        switch (action.JumpMode)
                        {
                            case JumpMode.CallAndStop:
                            case JumpMode.CallAndResume:
                                var targetMacro = MacroNavigation.FindSystemTargetMacro(host.Macros, action);
                                stopAfterJump = action.JumpMode == JumpMode.CallAndStop;
                                await ExecuteAsync(targetMacro, cancellationToken, callDepth + 1,
                                    triggerEvent: triggerEvent, logIndentLevel: actionLogIndentLevel,
                                    actionContextToken: actionContextToken);
                                if (stopAfterJump)
                                    nextActionIndex = actionIds.Length;
                                break;
                            case JumpMode.GotoAction:
                                if (onlyActionId is not null)
                                    throw new InvalidOperationException(
                                        "Un Jump vers une action doit être exécuté avec toute sa macro.");
                                var targetAction = MacroNavigation.FindJumpTargetAction(macro, action);
                                nextActionIndex = Array.IndexOf(actionIds, targetAction.Id);
                                if (nextActionIndex < 0)
                                    throw new InvalidOperationException("L'action cible de Jump n'est plus dans la séquence.");
                                break;
                            default:
                                throw new InvalidOperationException("Le mode de l'action Jump est invalide.");
                        }
                        break;
                    case MacroActionKind.If:
                        var condition = EvaluateIf(
                            ResolveIfLeft(action, previousActionResult, triggerEvent), action,
                            host.ExpandTemplate(action.Value, previousActionResult, triggerEvent));
                        actionContext.Result = condition;
                        if (!condition)
                            nextActionIndex++;
                        break;
                    case MacroActionKind.IfJump:
                        var ifJumpCondition = EvaluateIf(
                            ResolveIfLeft(action, previousActionResult, triggerEvent), action,
                            host.ExpandTemplate(action.Value, previousActionResult, triggerEvent));
                        actionContext.Result = ifJumpCondition;
                        var branchMode = (ifJumpCondition ? action.IfBranchMode : action.ElseBranchMode)
                            ?? ConditionalJumpMode.GotoAction;
                        switch (branchMode)
                        {
                            case ConditionalJumpMode.Continue:
                                break;
                            case ConditionalJumpMode.Stop:
                                nextActionIndex = actionIds.Length;
                                break;
                            case ConditionalJumpMode.GotoAction:
                                if (onlyActionId is not null)
                                    throw new InvalidOperationException(
                                        "Un If / Jump vers une action doit être exécuté avec toute sa macro.");
                                var branchTarget = MacroNavigation.FindConditionalJumpTargetAction(macro,
                                    ifJumpCondition ? action.Target : action.ElseTarget,
                                    ifJumpCondition ? "If" : "Else");
                                nextActionIndex = Array.IndexOf(actionIds, branchTarget.Id);
                                if (nextActionIndex < 0)
                                    throw new InvalidOperationException(
                                        "L'action cible de If / Jump n'est plus dans la séquence.");
                                break;
                            case ConditionalJumpMode.GotoMacro:
                                var branchMacro = MacroNavigation.FindSystemTargetMacro(host.Macros,
                                    ifJumpCondition ? action.Target : action.ElseTarget);
                                await ExecuteAsync(branchMacro, cancellationToken, callDepth + 1,
                                    triggerEvent: triggerEvent, logIndentLevel: actionLogIndentLevel,
                                    actionContextToken: actionContextToken);
                                nextActionIndex = actionIds.Length;
                                break;
                            default:
                                throw new InvalidOperationException("Le mode de l'action If / Jump est invalide.");
                        }
                        break;
                    default:
                        throw new InvalidOperationException($"Type d'action non pris en charge : {action.Kind}");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (action.Kind == MacroActionKind.Plugin ||
                                               exception is InvalidOperationException or IOException or JsonException or
                                               TimeoutException or UnauthorizedAccessException or
                                               Win32Exception or SecurityException)
            {
                actionContext.Log($"Erreur dans « {macro.Name} » ({action.Kind}) : {exception.Message}", LogLevel.Error);
                actionContext.Result = null;
            }

            if (stopAfterJump)
                break;
            previousActionResult = actionContext.Result;
            actionIndex = nextActionIndex;
        }
        if (!cancellationToken.IsCancellationRequested)
            host.Log(selectedAction is null
                    ? $"Macro terminée : « {macro.Name} »."
                    : $"Action terminée : « {selectedAction.DisplayName} ».",
                LogLevel.Information,
                selectedAction is null ? MacroLogCategory.Macro : MacroLogCategory.Action,
                entryIndentLevel);
    }

    private static int NextLogIndentLevel(int level) => Math.Min(level + 1, MaximumLogIndentLevel);

    private static bool EvaluateIf(string? left, MacroAction action, string expected)
    {
        var comparison = action.IfComparison switch
        {
            IfComparison.Equals => IfOperator.Equals,
            IfComparison.Contains => IfOperator.Contains,
            IfComparison.NotEquals => IfOperator.NotEquals,
            _ => throw new InvalidOperationException("La comparaison de l'action If est invalide.")
        };
        return MacroConditionEvaluator.Evaluate(left, comparison, expected);
    }

    private string? ResolveIfLeft(MacroAction action, object? previousResult, TriggerEvent? triggerEvent) =>
        string.IsNullOrWhiteSpace(action.IfLeft)
            ? MacroValueFormatter.Format(previousResult)
            : host.ExpandTemplate(action.IfLeft, previousResult, triggerEvent);
}
