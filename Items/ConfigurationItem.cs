using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace EventGhostDotNet;

public abstract class ConfigurationItem : INotifyPropertyChanged
{
    private ConfigurationContainer? _parent;

    [JsonIgnore]
    public ConfigurationContainer? Parent
    {
        get => _parent;
        internal set
        {
            if (ReferenceEquals(_parent, value))
                return;
            _parent = value;
            OnParentChanged();
            NotifyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void NotifyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected virtual void OnParentChanged() { }
}

public abstract class ConfigurationContainer : ConfigurationItem
{
    public abstract IEnumerable<ConfigurationItem> GetChildren();
}
