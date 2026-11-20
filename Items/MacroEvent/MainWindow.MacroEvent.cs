using System.Runtime.InteropServices;
using System.Windows;
using EventGhostDotNet.PluginContracts;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;

namespace EventGhostDotNet;

public partial class MainWindow
{
    private readonly Dictionary<(string SourceId, string EventName), Task> _sourceEventTails = [];
    private readonly object _sourceEventLock = new();

    private async void SimulateEvent_Click(object sender, RoutedEventArgs e)
    {
        var eventName = PromptForText("Déclencher un événement", "Nom exact de l'événement :", "Demo.Start");
        if (string.IsNullOrWhiteSpace(eventName))
            return;

        await DispatchEventAsync(eventName.Trim(), null);
    }

    private async Task DispatchEventAsync(
        string eventName,
        string? payload,
        CancellationToken? sessionToken = null,
        string? sourceId = null)
    {
        var cancellationToken = sessionToken ?? _sessionCancellation.Token;
        if (cancellationToken.IsCancellationRequested)
            return;

        // Chain events of the same kind from one source so scripts observe them in arrival order.
        var key = (SourceId: sourceId ?? string.Empty, EventName: eventName.ToUpperInvariant());
        Task? previous = null;
        TaskCompletionSource? completed = null;
        if (!string.IsNullOrEmpty(sourceId))
        {
            lock (_sourceEventLock)
            {
                _sourceEventTails.TryGetValue(key, out previous);
                completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _sourceEventTails[key] = completed.Task;
            }
        }

        try
        {
            if (previous is not null)
                await previous;
            if (cancellationToken.IsCancellationRequested)
                return;

            if (string.Equals(eventName, SystemShowEventName, StringComparison.OrdinalIgnoreCase))
            {
                ShowMainWindow();
            }
            else if (string.Equals(eventName, SystemHideEventName, StringComparison.OrdinalIgnoreCase))
            {
                Hide();
            }

            var exitAfterEvent = string.Equals(eventName, SystemExitEventName, StringComparison.OrdinalIgnoreCase);
            var matchingMacros = Macros
                .Where(macro => macro.IsEnabled && macro.MatchesEvent(eventName))
                .ToList();
            var detail = string.IsNullOrWhiteSpace(payload) ? string.Empty : $" — {payload}";
            WriteLog($"Événement reçu : {eventName}{detail} — {matchingMacros.Count} macro(s) correspondante(s).", eventName);
            foreach (var macro in matchingMacros)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;
                if (!macro.IsEnabled)
                    continue;
                await ExecuteMacroInSessionAsync(macro, cancellationToken,
                    triggerEvent: new TriggerEvent(eventName, payload, sourceId));
            }
            if (exitAfterEvent && !cancellationToken.IsCancellationRequested)
            {
                RequestExit();
            }
        }
        finally
        {
            if (completed is not null)
            {
                lock (_sourceEventLock)
                {
                    completed.TrySetResult();
                    if (_sourceEventTails.TryGetValue(key, out var tail) && ReferenceEquals(tail, completed.Task))
                        _sourceEventTails.Remove(key);
                }
            }
        }
    }

    internal void EditEventItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MacroEvent macroEvent })
        {
            SelectTreeItem(macroEvent.Macro, macroEvent);
            EditEvent(macroEvent);
        }
    }

    internal void RemoveEventItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MacroEvent macroEvent })
        {
            SelectTreeItem(macroEvent.Macro, macroEvent);
            RemoveEvent(macroEvent);
        }
    }

    private void EditEvent(MacroEvent macroEvent)
    {
        var macro = macroEvent.Macro;
        var name = PromptForText("Modifier l'événement", "Nom exact de l'événement :", macroEvent.Name);
        if (name is null)
            return;

        name = name.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, "Saisissez un nom d'événement.", "Événement requis",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var index = macro.EventItems.IndexOf(macroEvent);
        if (index < 0)
            return;
        if (macro.EventNames.Where((_, i) => i != index)
            .Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Cet événement est déjà associé à la macro.", "Événement déjà présent",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var previousName = macroEvent.Name;
        macro.EventNames[index] = name;
        WriteLog($"Événement « {previousName} » modifié en « {name} » dans « {macro.Name} ».");
    }

    private void RemoveEvent(MacroEvent macroEvent)
    {
        var macro = macroEvent.Macro;
        if (MessageBox.Show(this, $"Supprimer l'événement « {macroEvent.Name} » de la macro « {macro.Name} » ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        var index = macro.EventItems.IndexOf(macroEvent);
        if (index < 0)
            return;
        macro.EventNames.RemoveAt(index);
        SelectTreeItem(macro);
        WriteLog($"Événement « {macroEvent.Name} » supprimé de « {macro.Name} ».");
    }

    private void CopyEventName_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedEventName() is not string eventName)
            return;

        try
        {
            Clipboard.SetText(eventName);
        }
        catch (ExternalException exception)
        {
            WriteLog($"Impossible de copier le nom de l'événement : {exception.Message}", LogLevel.Error);
            MessageBox.Show(this, exception.Message, "Erreur de presse-papiers", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AssignEventName_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedEventName() is not string eventName)
            return;
        if (_selectedMacro is not MacroDefinition macro)
        {
            MessageBox.Show(this, "Créez ou sélectionnez d'abord une macro.", "Aucune macro sélectionnée",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (macro.EventNames.Contains(eventName, StringComparer.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Cet événement est déjà associé à la macro.", "Événement déjà présent",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, $"Ajouter « {eventName} » aux déclencheurs de « {macro.Name} » ?",
                "Ajouter un déclencheur", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        AddEventToMacro(macro, eventName);
    }

    private void AddEventToMacro(MacroDefinition macro, string eventName)
    {
        macro.EventNames.Add(eventName);
        WriteLog($"Événement « {eventName} » ajouté à la macro « {macro.Name} ».");
    }

    private string? GetSelectedEventName()
    {
        if (LogList.SelectedItems.Count == 1 &&
            LogList.SelectedItem is EventLogEntry entry && !string.IsNullOrWhiteSpace(entry.EventName))
            return entry.EventName;

        MessageBox.Show(this, "Sélectionnez un seul événement reçu dans le journal.", "Sélection d'événement",
            MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }
}
