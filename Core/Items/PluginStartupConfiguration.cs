using System.Text.Json.Serialization;

namespace EventGhostDotNet;

public sealed class PluginStartupConfiguration : ConfigurationItem
{
    private string _pluginId = string.Empty;
    private string _configurationId = string.Empty;
    private string _instanceId = Guid.NewGuid().ToString();
    private string _value = string.Empty;
    private string _target = string.Empty;
    private string _displayName = string.Empty;
    private bool _canConfigure;
    private bool _isEnabled = true;

    public string PluginId
    {
        get => _pluginId;
        set
        {
            if (_pluginId == value)
                return;
            _pluginId = value;
            NotifyChanged();
        }
    }

    public string ConfigurationId
    {
        get => _configurationId;
        set
        {
            if (_configurationId == value)
                return;
            _configurationId = value;
            NotifyChanged();
        }
    }

    public string InstanceId
    {
        get => _instanceId;
        set
        {
            if (_instanceId == value)
                return;
            _instanceId = value;
            NotifyChanged();
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value)
                return;
            _value = value;
            NotifyChanged();
        }
    }

    public string Target
    {
        get => _target;
        set
        {
            if (_target == value)
                return;
            _target = value;
            NotifyChanged();
        }
    }

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
