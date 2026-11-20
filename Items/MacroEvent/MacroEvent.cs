namespace EventGhostDotNet;

public sealed class MacroEvent : ConfigurationItem
{
    internal MacroEvent(MacroDefinition macro, string name)
    {
        Macro = macro;
        Name = name;
    }

    public MacroDefinition Macro { get; }
    public string Name { get; private set; }
    public bool IsEnabled
    {
        get => !Macro.DisabledEventNames.Contains(Name, StringComparer.OrdinalIgnoreCase);
        set
        {
            if (IsEnabled == value)
                return;
            if (value)
                Macro.RemoveDisabledEvent(Name);
            else if (!Macro.DisabledEventNames.Contains(Name, StringComparer.OrdinalIgnoreCase))
                Macro.DisabledEventNames.Add(Name);
            NotifyChanged();
        }
    }

    internal void SetName(string name)
    {
        if (Name == name)
            return;
        var wasEnabled = IsEnabled;
        if (!wasEnabled)
        {
            Macro.RemoveDisabledEvent(Name);
            if (!Macro.DisabledEventNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                Macro.DisabledEventNames.Add(name);
        }
        Name = name;
        NotifyChanged(nameof(Name));
    }
}
