using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using EventGhostDotNet.PluginContracts;
using DragDrop = System.Windows.DragDrop;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Brushes = System.Windows.Media.Brushes;

namespace EventGhostDotNet;

public partial class MainWindow
{
    private const string TreeDragFormat = "EventGhostDotNet.ConfigurationTreeItem";
    private const string DefaultStatus = "Moteur prêt — les événements peuvent déclencher les macros associées.";
    private object? _treeDragCandidate;
    private object? _treeDragSource;
    private Point _treeDragStart;
    private EventLogEntry? _logDragCandidate;
    private Point _logDragStart;
    private TreeDropAdorner? _treeDropAdorner;
    private AdornerLayer? _treeDropAdornerLayer;
    private long _lastTreeDragScroll;

    private enum TreeDropPosition { Before, After, Inside }

    private sealed record TreeDropPlan(object Source, object Target, TreeDropPosition Position);

    private void LogList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _logDragCandidate = null;
        if (e.ClickCount != 1 || e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(LogList, source) is not ListBoxItem { DataContext: EventLogEntry entry } ||
            string.IsNullOrWhiteSpace(entry.EventName))
            return;

        _logDragCandidate = entry;
        _logDragStart = e.GetPosition(LogList);
    }

    private void LogList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_logDragCandidate is null || e.LeftButton != MouseButtonState.Pressed)
        {
            _logDragCandidate = null;
            return;
        }

        var point = e.GetPosition(LogList);
        if (Math.Abs(point.X - _logDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _logDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var source = _logDragCandidate;
        _logDragCandidate = null;
        StartConfigurationDrag(LogList, source, DragDropEffects.Copy);
    }

    private void LogList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        _logDragCandidate = null;

    private void LogList_MouseLeave(object sender, MouseEventArgs e) =>
        _logDragCandidate = null;

    private void ConfigurationTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _treeDragCandidate = null;
        if (e.ClickCount != 1 || e.OriginalSource is not DependencyObject source ||
            FindTreeItem(source) is not TreeViewItem item ||
            ShouldIgnoreTreeItemDoubleClick(source, item))
            return;

        if (item.DataContext is MacroGroup or MacroDefinition or MacroEvent or MacroAction or PluginStartupConfiguration)
        {
            _treeDragCandidate = item.DataContext;
            _treeDragStart = e.GetPosition(ConfigurationTree);
        }
    }

    private void ConfigurationTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_treeDragCandidate is null || e.LeftButton != MouseButtonState.Pressed)
        {
            _treeDragCandidate = null;
            return;
        }

        var point = e.GetPosition(ConfigurationTree);
        if (Math.Abs(point.X - _treeDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _treeDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var source = _treeDragCandidate;
        _treeDragCandidate = null;
        StartConfigurationDrag(ConfigurationTree, source, DragDropEffects.Move);
    }

    private void StartConfigurationDrag(UIElement origin, object source, DragDropEffects effects)
    {
        _treeDragSource = source;
        var data = new System.Windows.DataObject(TreeDragFormat, _treeDragSource);
        try
        {
            DragDrop.DoDragDrop(origin, data, effects);
        }
        finally
        {
            _treeDragSource = null;
            ClearTreeDropFeedback();
            StatusText.Text = DefaultStatus;
        }
    }

    private void ConfigurationTree_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        _treeDragCandidate = null;

    private void ConfigurationTree_MouseLeave(object sender, MouseEventArgs e) =>
        _treeDragCandidate = null;

    private void ConfigurationTree_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;
        if (_treeDragSource is null || !e.Data.GetDataPresent(TreeDragFormat, false) ||
            !ReferenceEquals(e.Data.GetData(TreeDragFormat, false), _treeDragSource))
            return;

        ScrollTreeDuringDrag(e);
        if (!TryGetTreeDrop(e, out var plan, out var header, out var reason))
        {
            ClearTreeDropFeedback();
            StatusText.Text = reason;
            return;
        }

        e.Effects = plan!.Source is EventLogEntry ? DragDropEffects.Copy : DragDropEffects.Move;
        StatusText.Text = plan.Source is EventLogEntry
            ? "Ajouter cet événement à la macro."
            : plan.Position == TreeDropPosition.Inside
                ? "Déposer dans ce conteneur."
                : "Déposer à l'emplacement indiqué.";
        ShowTreeDropFeedback(header!, plan.Position);
    }

    private void ConfigurationTree_PreviewDragLeave(object sender, DragEventArgs e)
    {
        var point = e.GetPosition(ConfigurationTree);
        if (point.X >= 0 && point.Y >= 0 &&
            point.X < ConfigurationTree.ActualWidth && point.Y < ConfigurationTree.ActualHeight)
            return;
        ClearTreeDropFeedback();
        StatusText.Text = DefaultStatus;
    }

    private void ConfigurationTree_PreviewDrop(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;
        if (_treeDragSource is null || !e.Data.GetDataPresent(TreeDragFormat, false) ||
            !ReferenceEquals(e.Data.GetData(TreeDragFormat, false), _treeDragSource))
            return;

        if (TryGetTreeDrop(e, out var plan, out _, out var reason))
        {
            ExecuteTreeDrop(plan!);
            e.Effects = plan!.Source is EventLogEntry ? DragDropEffects.Copy : DragDropEffects.Move;
        }
        else
        {
            WriteLog($"Déplacement impossible : {reason}", LogLevel.Warning);
        }
        ClearTreeDropFeedback();
    }

    private bool TryGetTreeDrop(DragEventArgs e, out TreeDropPlan? plan, out FrameworkElement? header, out string reason)
    {
        plan = null;
        header = null;
        reason = "Déposez l'élément sur une ligne compatible.";
        if (e.OriginalSource is not DependencyObject source || FindTreeItem(source) is not TreeViewItem item)
            return false;

        header = item.Template.FindName("PART_Header", item) as FrameworkElement;
        if (header is null || header.ActualHeight <= 0)
            return false;
        var point = e.GetPosition(header);
        if (point.Y < 0 || point.Y > header.ActualHeight)
            return false;

        var target = item.DataContext;
        var inside = (_treeDragSource, target) switch
        {
            (MacroGroup, MacroGroup) => point.Y >= header.ActualHeight * 0.25 &&
                                        point.Y <= header.ActualHeight * 0.75,
            (MacroGroup, ConfigurationRoot) => true,
            (MacroDefinition, MacroGroup) => true,
            (MacroEvent, MacroDefinition) => true,
            (EventLogEntry, MacroDefinition) => true,
            (MacroAction, MacroDefinition or StartupSection) => true,
            (PluginStartupConfiguration, StartupSection) => true,
            _ => false
        };
        var position = inside ? TreeDropPosition.Inside :
            point.Y < header.ActualHeight / 2 ? TreeDropPosition.Before : TreeDropPosition.After;
        return TryCreateTreeDropPlan(_treeDragSource!, target, position, out plan, out reason);
    }

    private bool TryCreateTreeDropPlan(object source, object? target, TreeDropPosition position,
        out TreeDropPlan? plan, out string reason)
    {
        plan = null;
        reason = "Ce type d'élément ne peut pas être déposé ici.";
        if (target is null || ReferenceEquals(source, target))
            return false;

        switch (source)
        {
            case MacroGroup group when MacroGroups.Contains(group):
                var newParent = target switch
                {
                    ConfigurationRoot root when ReferenceEquals(root, _configurationRoot) &&
                                                position == TreeDropPosition.Inside => null,
                    MacroGroup targetGroup when MacroGroups.Contains(targetGroup) =>
                        position == TreeDropPosition.Inside ? targetGroup : targetGroup.Parent as MacroGroup,
                    _ => group
                };
                for (var ancestor = newParent; ancestor is not null; ancestor = ancestor.Parent as MacroGroup)
                {
                    if (ReferenceEquals(ancestor, group))
                    {
                        reason = "Un groupe ne peut pas être déplacé dans lui-même ou dans un de ses sous-groupes.";
                        return false;
                    }
                }
                break;
            case MacroDefinition macro when FindMacroGroup(macro) is MacroGroup sourceGroup &&
                                            sourceGroup.Macros.Contains(macro):
                if (target is MacroGroup destinationGroup && MacroGroups.Contains(destinationGroup) &&
                    position == TreeDropPosition.Inside)
                    break;
                if (target is MacroDefinition targetMacro && FindMacroGroup(targetMacro) is MacroGroup targetMacroGroup &&
                    targetMacroGroup.Macros.Contains(targetMacro) && position != TreeDropPosition.Inside)
                    break;
                return false;
            case PluginStartupConfiguration startup when PluginStartups.Contains(startup):
                if (target is StartupSection && ReferenceEquals(target, _startupSection) &&
                    position == TreeDropPosition.Inside)
                    break;
                if (target is PluginStartupConfiguration targetStartup && PluginStartups.Contains(targetStartup) &&
                    position != TreeDropPosition.Inside)
                    break;
                return false;
            case MacroEvent macroEvent when Macros.Contains(macroEvent.Macro) &&
                                            macroEvent.Macro.EventItems.Contains(macroEvent):
                var eventDestination = target switch
                {
                    MacroDefinition destination when Macros.Contains(destination) &&
                                                     position == TreeDropPosition.Inside => destination,
                    MacroEvent targetEvent when Macros.Contains(targetEvent.Macro) &&
                                                targetEvent.Macro.EventItems.Contains(targetEvent) &&
                                                position != TreeDropPosition.Inside => targetEvent.Macro,
                    _ => null
                };
                if (eventDestination is null)
                    return false;
                if (!ReferenceEquals(eventDestination, macroEvent.Macro) &&
                    eventDestination.EventNames.Contains(macroEvent.Name, StringComparer.OrdinalIgnoreCase))
                {
                    reason = "La macro de destination contient déjà cet événement.";
                    return false;
                }
                break;
            case EventLogEntry entry when !string.IsNullOrWhiteSpace(entry.EventName) &&
                                          target is MacroDefinition destination &&
                                          Macros.Contains(destination) && position == TreeDropPosition.Inside:
                if (destination.EventNames.Contains(entry.EventName, StringComparer.OrdinalIgnoreCase))
                {
                    reason = "La macro de destination contient déjà cet événement.";
                    return false;
                }
                break;
            case MacroAction action when FindActionOwner(action) is MacroDefinition owner:
                var actionDestination = GetDropActionOwner(target, position);
                if (actionDestination is null)
                    return false;
                if (!ReferenceEquals(owner, actionDestination) &&
                    !CanMoveActionBetweenContainers(action, owner, actionDestination, out reason))
                    return false;
                break;
            default:
                return false;
        }

        plan = new TreeDropPlan(source, target, position);
        reason = string.Empty;
        return true;
    }

    private MacroDefinition? FindActionOwner(MacroAction action) =>
        Macros.FirstOrDefault(macro => macro.Actions.Contains(action)) ??
        (StartupActions.Contains(action) ? _startupMacro : null);

    private MacroDefinition? GetDropActionOwner(object target, TreeDropPosition position) => target switch
    {
        MacroDefinition macro when Macros.Contains(macro) && position == TreeDropPosition.Inside => macro,
        StartupSection section when ReferenceEquals(section, _startupSection) &&
                                    position == TreeDropPosition.Inside => _startupMacro,
        MacroAction action when position != TreeDropPosition.Inside => FindActionOwner(action),
        _ => null
    };

    private bool CanMoveActionBetweenContainers(MacroAction action, MacroDefinition source,
        MacroDefinition destination, out string reason)
    {
        if (source.Actions.Any(other => !ReferenceEquals(other, action) &&
                other.Kind == MacroActionKind.Jump && other.JumpMode == JumpMode.GotoAction &&
                Guid.TryParse(other.Value, out var id) && id == action.Id))
        {
            reason = "Une action Jump de la macro actuelle cible cette action.";
            return false;
        }
        if (action.Kind == MacroActionKind.Jump && action.JumpMode == JumpMode.GotoAction &&
            (!Guid.TryParse(action.Value, out var targetId) ||
             (targetId != action.Id && !destination.Actions.Any(item => item.Id == targetId))))
        {
            reason = "L'action Jump doit conserver sa cible dans la même macro.";
            return false;
        }
        if (ReferenceEquals(destination, _startupMacro) &&
            GetActionReferences(source, action).Any(item =>
                item.Kind is MacroActionKind.EnableAction or MacroActionKind.DisableAction))
        {
            reason = "Une action système cible cette action ; elle ne peut pas être déplacée dans Démarrage.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private IEnumerable<MacroAction> GetActionReferences(MacroDefinition owner, MacroAction action) =>
        Macros.SelectMany(macro => macro.Actions).Concat(StartupActions)
            .Where(item => (item.Kind is MacroActionKind.EnableAction or MacroActionKind.DisableAction ||
                            item.Kind is MacroActionKind.ActivateItem or MacroActionKind.DeactivateItem &&
                            item.ActivationTarget == ActivationTargetKind.Action) &&
                           (ReferenceEquals(owner, _startupMacro)
                               ? string.Equals(item.Target, "Startup", StringComparison.OrdinalIgnoreCase)
                               : Guid.TryParse(item.Target, out var macroId) && macroId == owner.Id) &&
                           Guid.TryParse(item.Value, out var actionId) && actionId == action.Id);

    private void ExecuteTreeDrop(TreeDropPlan plan)
    {
        switch (plan.Source)
        {
            case EventLogEntry entry:
                var destinationMacro = (MacroDefinition)plan.Target;
                AddEventToMacro(destinationMacro, entry.EventName!);
                SelectTreeItem(destinationMacro, destinationMacro.EventItems[^1]);
                break;
            case MacroGroup group:
                var sourceGroups = GetGroupSiblings(group);
                var destinationGroups = plan.Target switch
                {
                    ConfigurationRoot => _configurationRoot.Groups,
                    MacroGroup destination when plan.Position == TreeDropPosition.Inside => destination.Groups,
                    MacroGroup sibling => GetGroupSiblings(sibling),
                    _ => throw new InvalidOperationException("Destination de groupe invalide.")
                };
                var groupIndex = plan.Target is MacroGroup siblingGroup && plan.Position != TreeDropPosition.Inside
                    ? DropIndex(destinationGroups.IndexOf(siblingGroup), plan.Position)
                    : destinationGroups.Count;
                if (!MoveTreeItem(sourceGroups, destinationGroups, group, groupIndex))
                    return;
                SynchronizeGroupOrder();
                SynchronizeMacroOrder();
                SelectMacroGroup(group);
                WriteLog($"Groupe « {group.Name} » déplacé à la position {destinationGroups.IndexOf(group) + 1}.");
                break;
            case MacroDefinition macro:
                var sourceGroup = FindMacroGroup(macro)!;
                var destinationGroup = plan.Target as MacroGroup ?? FindMacroGroup((MacroDefinition)plan.Target)!;
                var macroIndex = plan.Target is MacroDefinition targetMacro
                    ? DropIndex(destinationGroup.Macros.IndexOf(targetMacro), plan.Position)
                    : destinationGroup.Macros.Count;
                if (!MoveTreeItem(sourceGroup.Macros, destinationGroup.Macros, macro, macroIndex))
                    return;
                macro.GroupId = destinationGroup.Id;
                SynchronizeMacroOrder();
                SelectTreeItem(macro);
                WriteLog($"Macro « {macro.Name} » déplacée dans « {destinationGroup.Name} ».");
                break;
            case PluginStartupConfiguration startup:
                var startupIndex = plan.Target is PluginStartupConfiguration targetStartup
                    ? DropIndex(PluginStartups.IndexOf(targetStartup), plan.Position) : PluginStartups.Count;
                if (!MoveTreeItem(PluginStartups, PluginStartups, startup, startupIndex))
                    return;
                SelectPluginStartupItem(startup);
                WriteLog("Ordre des démarrages de plugins modifié ; il sera appliqué au prochain démarrage ou rechargement.");
                break;
            case MacroEvent macroEvent:
                var eventSource = macroEvent.Macro;
                var eventDestination = plan.Target is MacroEvent targetEvent ? targetEvent.Macro : (MacroDefinition)plan.Target;
                var eventIndex = plan.Target is MacroEvent siblingEvent
                    ? DropIndex(eventDestination.EventItems.IndexOf(siblingEvent), plan.Position)
                    : eventDestination.EventNames.Count;
                var wasEnabled = macroEvent.IsEnabled;
                var eventReferences = GetEventReferences(macroEvent).ToArray();
                if (!MoveTreeItem(eventSource.EventNames, eventDestination.EventNames, macroEvent.Name, eventIndex))
                    return;
                var movedEvent = ReferenceEquals(eventSource, eventDestination)
                    ? macroEvent
                    : eventDestination.EventItems[eventIndex];
                if (!ReferenceEquals(eventSource, eventDestination))
                {
                    movedEvent.IsEnabled = wasEnabled;
                    foreach (var reference in eventReferences)
                        reference.Target = eventDestination.Id.ToString();
                }
                RefreshSystemActionDescriptions(Macros.Append(_startupMacro));
                SelectTreeItem(eventDestination, movedEvent);
                WriteLog($"Événement « {macroEvent.Name} » déplacé dans « {eventDestination.Name} ».");
                break;
            case MacroAction action:
                var actionSource = FindActionOwner(action)!;
                var actionDestination = GetDropActionOwner(plan.Target, plan.Position)!;
                var actionIndex = plan.Target is MacroAction targetAction
                    ? DropIndex(actionDestination.Actions.IndexOf(targetAction), plan.Position)
                    : actionDestination.Actions.Count;
                var references = GetActionReferences(actionSource, action).ToArray();
                if (!MoveTreeItem(actionSource.Actions, actionDestination.Actions, action, actionIndex))
                    return;
                if (!ReferenceEquals(actionSource, actionDestination))
                    foreach (var reference in references)
                        reference.Target = ReferenceEquals(actionDestination, _startupMacro)
                            ? "Startup" : actionDestination.Id.ToString();
                RefreshSystemActionDescriptions(Macros.Append(_startupMacro));
                if (ReferenceEquals(actionDestination, _startupMacro))
                    SelectStartupActionItem(action);
                else
                    SelectTreeItem(actionDestination, action);
                WriteLog($"Action déplacée dans « {actionDestination.Name} ».");
                break;
        }
    }

    private static int DropIndex(int targetIndex, TreeDropPosition position) =>
        targetIndex + (position == TreeDropPosition.After ? 1 : 0);

    private static bool MoveTreeItem<T>(ObservableCollection<T> source, ObservableCollection<T> destination,
        T item, int destinationIndex)
    {
        var sourceIndex = source.IndexOf(item);
        if (ReferenceEquals(source, destination))
        {
            if (destinationIndex > sourceIndex)
                destinationIndex--;
            if (sourceIndex == destinationIndex)
                return false;
            source.Move(sourceIndex, destinationIndex);
        }
        else
        {
            source.RemoveAt(sourceIndex);
            destination.Insert(destinationIndex, item);
        }
        return true;
    }

    private void ScrollTreeDuringDrag(DragEventArgs e)
    {
        var viewer = FindScrollViewer(ConfigurationTree);
        if (viewer is null || Environment.TickCount64 - _lastTreeDragScroll < 80)
            return;
        var point = e.GetPosition(viewer);
        if (point.Y >= 0 && point.Y < 24)
            viewer.LineUp();
        else if (point.Y <= viewer.ActualHeight && point.Y > viewer.ActualHeight - 24)
            viewer.LineDown();
        else
            return;
        _lastTreeDragScroll = Environment.TickCount64;
    }

    private void ShowTreeDropFeedback(FrameworkElement header, TreeDropPosition position)
    {
        if (_treeDropAdorner is not null &&
            ReferenceEquals(_treeDropAdorner.AdornedElement, header) && _treeDropAdorner.Position == position)
            return;
        ClearTreeDropFeedback();
        _treeDropAdornerLayer = AdornerLayer.GetAdornerLayer(header);
        if (_treeDropAdornerLayer is null)
            return;
        _treeDropAdorner = new TreeDropAdorner(header, position) { IsHitTestVisible = false };
        _treeDropAdornerLayer.Add(_treeDropAdorner);
    }

    private void ClearTreeDropFeedback()
    {
        if (_treeDropAdorner is not null)
            _treeDropAdornerLayer?.Remove(_treeDropAdorner);
        _treeDropAdorner = null;
        _treeDropAdornerLayer = null;
    }

    private sealed class TreeDropAdorner(UIElement element, TreeDropPosition position) : Adorner(element)
    {
        public TreeDropPosition Position { get; } = position;

        protected override void OnRender(DrawingContext drawingContext)
        {
            var pen = new System.Windows.Media.Pen(Brushes.DodgerBlue, 2);
            if (Position == TreeDropPosition.Inside)
                drawingContext.DrawRectangle(null, pen, new Rect(1, 1, Math.Max(0, ActualWidth - 2), Math.Max(0, ActualHeight - 2)));
            else
            {
                var y = Position == TreeDropPosition.Before ? 1 : Math.Max(1, ActualHeight - 1);
                drawingContext.DrawLine(pen, new Point(0, y), new Point(ActualWidth, y));
            }
        }
    }
}
