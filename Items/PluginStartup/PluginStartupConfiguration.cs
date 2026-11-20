using System.Text.Json.Serialization;

namespace EventGhostDotNet;

public sealed class PluginStartupConfiguration : ConfigurationItem
{
    private string _displayName = string.Empty;
    private bool _canConfigure;
    private bool _isEnabled = true;

    public string PluginId { get; set; } = string.Empty;
    public string ConfigurationId { get; set; } = string.Empty;
    public string InstanceId { get; set; } = Guid.NewGuid().ToString();
    public string Value { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
                return;
            _isEnabled = value;
            NotifyChanged();
        }
    }

    [JsonIgnore]
    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (_displayName == value)
                return;
            _displayName = value;
            NotifyChanged();
        }
    }

    [JsonIgnore]
    public bool CanConfigure
    {
        get => _canConfigure;
        set
        {
            if (_canConfigure == value)
                return;
            _canConfigure = value;
            NotifyChanged();
        }
    }

}
