using System.Text.Json.Serialization;
using EventGhostDotNet.PluginContracts;

namespace EventGhostDotNet;

public sealed class MacroAction : ConfigurationItem
{
    private bool _isEnabled = true;
    private string? _systemDisplayName;
    private string? _pluginDisplayName;

    public Guid Id { get; set; } = Guid.NewGuid();
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

    public MacroActionKind Kind { get; set; }
    public string Value { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ActivationTargetKind? ActivationTarget { get; set; }
    public string PluginId { get; set; } = string.Empty;
    public string ActionId { get; set; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool UnlimitedExecution { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool UseWindowsPowerShell { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LogLevel? LogLevel { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InstanceId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NotificationKey { get; set; }

    [JsonIgnore]
    public string? PluginDisplayName
    {
        get => _pluginDisplayName;
        set
        {
            if (_pluginDisplayName == value)
                return;
            _pluginDisplayName = value;
            NotifyChanged(nameof(DisplayName));
        }
    }

    [JsonIgnore]
    public string? SystemDisplayName
    {
        get => _systemDisplayName;
        set
        {
            if (_systemDisplayName == value)
                return;
            _systemDisplayName = value;
            NotifyChanged(nameof(DisplayName));
        }
    }

    [JsonIgnore]
    public string DisplayName => Kind switch
    {
        MacroActionKind.Log => $"Journaliser : {Value}",
        MacroActionKind.Delay => $"Attendre : {Value} ms",
        MacroActionKind.Beep => "Émettre un signal sonore",
        MacroActionKind.RaiseEvent => $"Déclencher l'événement : {Target}",
        MacroActionKind.ShowMessage => $"Afficher un message : {Value}",
        MacroActionKind.Notify => $"Afficher une notification : {Value.Split(['\r', '\n'], 2)[0]}",
        MacroActionKind.AskYesNo => $"Question Oui/Non : {Value}",
        MacroActionKind.Stop => "Arrêter la macro",
        MacroActionKind.OpenUrl => $"Ouvrir l'URL : {Value}",
        MacroActionKind.PowerShell => $"Script PowerShell : {Value.Split(['\r', '\n'], 2)[0]}",
        MacroActionKind.DisplayInputSource => $"Entrée écran : {Target} → {Value}",
        MacroActionKind.LogitechStart => $"Logitech — démarrer l'écoute : {Value}",
        MacroActionKind.LogitechChangeHost => $"Logitech — changer d'hôte : {Value}",
        MacroActionKind.LogitechSendRaw => $"Logitech — envoyer HID : {Value}",
        MacroActionKind.Plugin => PluginDisplayName ?? $"{PluginId} — {ActionId} : {Value}",
        MacroActionKind.EnableMacro or MacroActionKind.DisableMacro or
        MacroActionKind.EnableAction or MacroActionKind.DisableAction or
        MacroActionKind.ActivateItem or MacroActionKind.DeactivateItem =>
            SystemDisplayName ?? "Action système : cible indisponible",
        MacroActionKind.Jump or MacroActionKind.If or MacroActionKind.IfJump =>
            SystemDisplayName ?? "Branchement système : configuration indisponible",
        _ => Kind.ToString()
    };

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JumpMode? JumpMode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IfComparison? IfComparison { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IfLeft { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ConditionalJumpMode? IfBranchMode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ConditionalJumpMode? ElseBranchMode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ElseTarget { get; set; }
}

public enum MacroActionKind
{
    Log,
    Delay,
    Beep,
    ShowMessage,
    Notify,
    AskYesNo,
    Stop,
    OpenUrl,
    PowerShell,
    DisplayInputSource,
    LogitechStart,
    LogitechChangeHost,
    LogitechSendRaw,
    Plugin,
    EnableMacro,
    DisableMacro,
    EnableAction,
    DisableAction,
    Jump,
    If,
    RaiseEvent,
    ActivateItem,
    DeactivateItem,
    IfJump
}

public enum ActivationTargetKind
{
    Group,
    Macro,
    Event,
    Action,
    PluginStartup
}

public enum JumpMode
{
    CallAndStop,
    CallAndResume,
    GotoAction
}

public enum IfComparison
{
    Equals,
    Contains,
    NotEquals
}

public enum ConditionalJumpMode
{
    GotoAction,
    Continue,
    Stop
}
