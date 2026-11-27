using System.Diagnostics;
using System.Windows;
using EventGhostDotNet.Core.Macros;
using EventGhostDotNet.PluginContracts;
using MessageBox = System.Windows.MessageBox;

namespace EventGhostDotNet;

public partial class MainWindow : IMacroExecutionHost
{
    private readonly MacroEngine _macroEngine;

    IReadOnlyList<MacroGroup> IMacroExecutionHost.Groups => MacroGroups;
    IReadOnlyList<MacroDefinition> IMacroExecutionHost.Macros => Macros;
    IReadOnlyList<MacroAction> IMacroExecutionHost.StartupActions => StartupActions;
    IReadOnlyList<PluginStartupConfiguration> IMacroExecutionHost.PluginStartups => PluginStartups;

    void IMacroExecutionHost.Log(string message, LogLevel level, MacroLogCategory category, int indentLevel) =>
        WriteLog(message, null, level: level,
            category: category == MacroLogCategory.Macro ? LogCategory.Macro : LogCategory.Action,
            indentLevel: indentLevel);

    string IMacroExecutionHost.ExpandTemplate(
        string template, object? previousResult, TriggerEvent? triggerEvent) =>
        ExpandActionTemplate(template, previousResult, triggerEvent);

    MacroExecutionContext IMacroExecutionHost.CreateContext(
        CancellationToken sessionToken, object? initialResult, TriggerEvent? triggerEvent, int? logIndentLevel) =>
        CreateActionContext(sessionToken, initialResult, triggerEvent, logIndentLevel);

    void IMacroExecutionHost.ShowMessage(string title, string message) =>
        MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    bool IMacroExecutionHost.AskYesNo(string title, string message) =>
        MessageBox.Show(this, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    void IMacroExecutionHost.ShowNotification(string title, string message, string? key)
    {
        if (System.Windows.Application.Current is not App app)
            throw new InvalidOperationException("L'application de notification n'est pas disponible.");
        app.ShowNotification(title, message, key);
    }

    void IMacroExecutionHost.Beep() => System.Media.SystemSounds.Beep.Play();

    void IMacroExecutionHost.OpenUrl(Uri uri) =>
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });

    Task IMacroExecutionHost.RunPowerShellAsync(
        MacroAction action, MacroExecutionContext context, CancellationToken cancellationToken) =>
        RunPowerShellAsync(action.Value, action.UnlimitedExecution, action.UseWindowsPowerShell,
            context, cancellationToken);

    async Task IMacroExecutionHost.RunPluginActionAsync(
        MacroAction action, MacroExecutionContext context, CancellationToken cancellationToken)
    {
        var plugin = _pluginRuntime.Find(action.PluginId)
            ?? throw new InvalidOperationException($"Plugin introuvable : {action.PluginId}");
        EnsurePluginActionEnabled(plugin, action);
        await plugin.ExecuteActionAsync(ResolvePluginAction(plugin, action),
            new PluginHost(this, context), cancellationToken);
    }

    void IMacroExecutionHost.ApplySystemActionResult(SystemActionResult result) =>
        _cleanConfiguration = AcceptRuntimeStateChange(_cleanConfiguration, result);

    void IMacroExecutionHost.OnPluginStartupStateChanged()
    {
        _sessionCancellation.Cancel();
        _ = Dispatcher.BeginInvoke(new Action(() => _ = RestartConfiguredPluginsAsync()));
    }
}
