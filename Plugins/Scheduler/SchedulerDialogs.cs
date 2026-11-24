using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using EventGhostDotNet.PluginContracts;
using EventGhostDotNet.Plugins.Shared;

namespace EventGhostDotNet.Plugins.Scheduler;

internal static class SchedulerDialogs
{
    internal sealed record ActionValues(string Id, int Seconds, string EventName, string Payload, Guid MacroId);
    private sealed record ScheduleKind(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    private static readonly ScheduleKind[] Kinds =
    [
        new("Daily", "Quotidienne"),
        new("Weekly", "Hebdomadaire"),
        new("Once", "Date précise"),
        new("Idle", "Inactivité Windows")
    ];

    public static List<SchedulerModule.Schedule>? ConfigureSchedules(
        Window owner, IReadOnlyList<SchedulerModule.Schedule> initial)
    {
        var schedules = initial.ToList();
        var dialog = new Window
        {
            Title = "Planifications",
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 680,
            Height = 460,
            MinWidth = 540,
            MinHeight = 350,
            Padding = new Thickness(18)
        };
        var panel = new DockPanel();
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(footer, Dock.Bottom);
        footer.Children.Add(new Button { Content = "Annuler", IsCancel = true, MinWidth = 90 });
        var save = new Button { Content = "Enregistrer", IsDefault = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        save.Click += (_, _) => dialog.DialogResult = true;
        footer.Children.Add(save);
        panel.Children.Add(footer);

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(toolbar, Dock.Top);
        var kind = new ComboBox { Width = 160, ItemsSource = Kinds, SelectedIndex = 0 };
        toolbar.Children.Add(kind);
        var add = new Button { Content = "Ajouter", Margin = new Thickness(8, 0, 0, 0), MinWidth = 80 };
        var edit = new Button { Content = "Modifier", Margin = new Thickness(8, 0, 0, 0), MinWidth = 80 };
        var remove = new Button { Content = "Supprimer", Margin = new Thickness(8, 0, 0, 0), MinWidth = 80 };
        toolbar.Children.Add(add);
        toolbar.Children.Add(edit);
        toolbar.Children.Add(remove);
        panel.Children.Add(toolbar);
        var list = new ListBox { Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(list);
        void Refresh(int selectedIndex)
        {
            list.ItemsSource = schedules.Select(SchedulerModule.DescribeSchedule).ToArray();
            list.SelectedIndex = selectedIndex;
        }
        add.Click += (_, _) =>
        {
            var selectedKind = (ScheduleKind)kind.SelectedItem;
            var item = ConfigureSchedule(dialog, new SchedulerModule.Schedule(selectedKind.Id, "08:00", "1,2,3,4,5",
                DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 300, "Schedule.Event"));
            if (item is null)
                return;
            schedules.Add(item);
            Refresh(schedules.Count - 1);
        };
        edit.Click += (_, _) =>
        {
            var index = list.SelectedIndex;
            if (index < 0)
                return;
            var item = ConfigureSchedule(dialog, schedules[index]);
            if (item is null)
                return;
            schedules[index] = item;
            Refresh(index);
        };
        remove.Click += (_, _) =>
        {
            var index = list.SelectedIndex;
            if (index < 0)
                return;
            schedules.RemoveAt(index);
            Refresh(Math.Min(index, schedules.Count - 1));
        };
        Refresh(-1);
        dialog.Content = panel;
        return dialog.ShowDialog() == true ? schedules : null;
    }

    private static SchedulerModule.Schedule? ConfigureSchedule(Window owner, SchedulerModule.Schedule schedule)
    {
        var fields = new List<StartupConfigurationField>();
        if (schedule.Kind != "Idle")
            fields.Add(new("Heure locale (HH:mm) :", schedule.Time));
        if (schedule.Kind == "Weekly")
            fields.Add(new("Jours (0 = dimanche, 1 = lundi, …, 6 = samedi; séparés par des virgules) :", schedule.Days));
        if (schedule.Kind == "Once")
            fields.Add(new("Date locale (AAAA-MM-JJ) :", schedule.Date));
        if (schedule.Kind == "Idle")
            fields.Add(new("Inactivité clavier/souris (secondes) :", schedule.IdleSeconds.ToString(CultureInfo.InvariantCulture)));
        fields.Add(new("Nom de l'événement :", schedule.EventName));

        SchedulerModule.Schedule? Parse(string[] values)
        {
            var index = 0;
            var time = schedule.Kind == "Idle" ? "" : values[index++];
            var days = schedule.Kind == "Weekly" ? values[index++].Replace(" ", "") : "";
            var date = schedule.Kind == "Once" ? values[index++] : "";
            var idle = 0;
            if (schedule.Kind == "Idle" &&
                !int.TryParse(values[index++], NumberStyles.None, CultureInfo.InvariantCulture, out idle))
                return null;
            return new SchedulerModule.Schedule(schedule.Kind, time, days, date, idle, values[index]);
        }

        var result = StartupConfigurationDialog.Show(owner, "Modifier la planification", fields, values =>
        {
            var parsed = Parse(values);
            return parsed is null ? "La durée d'inactivité doit être un nombre entier." : SchedulerModule.ValidateSchedule(parsed);
        });
        return result is null ? null : Parse(result);
    }

    public static ActionValues? ConfigureAction(
        IPluginHost host, string actionId, string id, int seconds, string eventName, string payload, Guid macroId,
        Func<string, string, int, string, Guid, string?> validate)
    {
        var dialog = new Window
        {
            Title = "Configurer l'action du planificateur",
            Owner = host.OwnerWindow(),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            Padding = new Thickness(18)
        };
        var panel = new StackPanel { Width = 480 };
        TextBox AddText(string label, string value)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 5) });
            var box = new TextBox { Text = value, Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(box);
            return box;
        }
        var idBox = AddText("Identifiant de la tâche (plusieurs minuteries peuvent le partager) :", id);
        TextBox? secondsBox = null;
        TextBox? eventBox = null;
        TextBox? payloadBox = null;
        ComboBox? macroBox = null;
        if (actionId != "Cancel")
        {
            secondsBox = AddText(actionId == "Repeat" ? "Intervalle (secondes) :" : "Délai (secondes) :",
                seconds.ToString(CultureInfo.InvariantCulture));
            if (actionId == "DelayedMacro")
            {
                panel.Children.Add(new TextBlock { Text = "Macro à exécuter :", Margin = new Thickness(0, 0, 0, 5) });
                macroBox = new ComboBox
                {
                    ItemsSource = host.GetMacros(),
                    DisplayMemberPath = nameof(PluginMacroDescriptor.Name),
                    SelectedValuePath = nameof(PluginMacroDescriptor.Id),
                    SelectedValue = macroId,
                    Margin = new Thickness(0, 0, 0, 12)
                };
                panel.Children.Add(macroBox);
            }
            else
            {
                eventBox = AddText("Nom de l'événement :", eventName);
                payloadBox = AddText("Payload (facultatif) :", payload);
            }
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new Button { Content = "Annuler", IsCancel = true, MinWidth = 90 });
        var save = new Button { Content = "Enregistrer", IsDefault = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        ActionValues? result = null;
        save.Click += (_, _) =>
        {
            var parsedSeconds = 0;
            if (secondsBox is not null &&
                !int.TryParse(secondsBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out parsedSeconds))
            {
                MessageBox.Show(dialog, "La durée doit être un nombre entier.", "Configuration invalide",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var selectedMacro = macroBox?.SelectedItem is PluginMacroDescriptor selected ? selected.Id : Guid.Empty;
            var values = new ActionValues(idBox.Text.Trim(), parsedSeconds, eventBox?.Text.Trim() ?? "",
                payloadBox?.Text ?? "", selectedMacro);
            var error = validate(actionId, values.Id, values.Seconds, values.EventName, values.MacroId);
            if (error is not null)
            {
                MessageBox.Show(dialog, error, "Configuration invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            result = values;
            dialog.DialogResult = true;
        };
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        return dialog.ShowDialog() == true ? result : null;
    }
}
