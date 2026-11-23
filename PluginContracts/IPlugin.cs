namespace EventGhostDotNet.PluginContracts;

public sealed record PluginActionData(string ActionId, string Value, string Target);

public sealed record PluginActionDescriptor(string Id, string Name);

public sealed record PluginStartupData(string ConfigurationId, string InstanceId, string Value, string Target);

public sealed record PluginTrayMenuItem(string Label, string EventName, bool IsSeparator = false);

public sealed record PluginMacroDescriptor(Guid Id, string Name);

public enum LogLevel
{
    Debug,
    Information,
    Warning,
    Error
}

public interface IActionContext
{
    object? Result
    {
        get => null;
        set => throw new NotSupportedException("Ce contexte ne prend pas en charge les résultats d'action.");
    }

    void Log(string message);
    void Log(string message, LogLevel level)
    {
        if (level != LogLevel.Information)
            throw new NotSupportedException("Ce contexte ne prend pas en charge les niveaux de journal.");
        Log(message);
    }
    void RaiseEvent(string name, string? payload);
}

/// <summary>
/// Services d'interface fournis aux plugins. Le contrat ne référence pas WPF :
/// l'application fournit une implémentation WPF et un hôte de test peut fournir une implémentation inerte.
/// </summary>
public interface IPluginUserInterface
{
    /// <summary>Fenêtre native de l'application, ou <c>null</c> pour un hôte sans interface.</summary>
    object? NativeOwner { get; }

    /// <summary>Affiche une boîte de dialogue modale et retourne son résultat, ou <c>null</c> si elle a été annulée.</summary>
    bool? ShowDialog(object dialog);

    void ShowMessage(string message, string title);

    bool Confirm(string message, string title);
}

public interface IPluginHost : IActionContext
{
    IPluginUserInterface UserInterface { get; }

    /// <summary>Inactivité système, ou <c>null</c> si la plateforme ne l'expose pas.</summary>
    IIdleTimeProvider? IdleTime => null;

    /// <summary>Moniteurs DDC/CI, ou <c>null</c> si la plateforme ne les expose pas.</summary>
    IMonitorService? Monitors => null;

    /// <summary>Interfaces HID, ou <c>null</c> si la plateforme ne les expose pas.</summary>
    IHidDeviceService? HidDevices => null;

    /// <summary>État de session (verrouillage, veille), ou <c>null</c> si la plateforme ne l'expose pas.</summary>
    ISessionState? Session => null;
    IReadOnlyList<PluginActionData> GetConfiguredActions(string pluginId);
    IReadOnlyList<PluginStartupData> GetPluginStartupConfigurations(string pluginId) => [];
    void SetTrayMenuItems(string pluginId, IReadOnlyList<PluginTrayMenuItem> items) { }
    IReadOnlyList<PluginMacroDescriptor> GetMacros() =>
        throw new NotSupportedException("Cet hôte ne permet pas de sélectionner une macro.");
    Task RunMacroAsync(Guid macroId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Cet hôte ne permet pas d'exécuter une macro.");
}

public interface IPlugin : IAsyncDisposable
{
    string Id { get; }
    string Name { get; }
    IReadOnlyList<PluginActionDescriptor> Actions { get; }
    bool HasStartupConfiguration => false;
    PluginStartupData CreateDefaultStartupConfiguration() =>
        new("Startup", Guid.NewGuid().ToString(), string.Empty, string.Empty);
    PluginStartupData? ConfigureStartup(PluginStartupData? existing, IPluginHost host) =>
        existing ?? CreateDefaultStartupConfiguration();
    string DescribeStartup(PluginStartupData configuration) => $"{Name} — démarrage";
    Task StartupAsync(PluginStartupData configuration, IPluginHost host, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    PluginActionData? ConfigureAction(string actionId, PluginActionData? existing, IPluginHost host);
    string DescribeAction(PluginActionData action);
    Task ExecuteActionAsync(PluginActionData action, IPluginHost host, CancellationToken cancellationToken);
    Task ActionChangedAsync(PluginActionData previous, PluginActionData updated) => Task.CompletedTask;
    Task ResetAsync();
}

public interface IPluginInstanceProvider
{
    bool IsInstanceConfiguration(string actionId);
    bool RequiresInstance(string actionId);
    string GetInstanceName(PluginActionData configuration);
    bool MatchesInstance(PluginActionData action, PluginActionData configuration);
    PluginActionData BindInstance(PluginActionData action, PluginActionData configuration);
    PluginActionData? ConfigureInstanceAction(
        string actionId, PluginActionData? existing, PluginActionData configuration, IPluginHost host);
}
