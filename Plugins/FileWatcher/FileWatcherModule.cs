using System.IO;
using System.Text.Json;
using EventGhostDotNet.PluginContracts;
using EventGhostDotNet.Plugins.Shared;
using System.Windows;

namespace EventGhostDotNet.Plugins.FileWatcher;

public sealed class FileWatcherModule : IPlugin
{
    private const string ConfigurationId = "Folder";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly List<FileSystemWatcher> _watchers = [];

    private sealed record FolderSettings(string Path, string Filter, string EventPrefix);

    public string Id => "FileWatcher";
    public string Name => "Surveillance de dossiers";
    public IReadOnlyList<PluginActionDescriptor> Actions { get; } = [];
    public bool HasStartupConfiguration => true;

    public PluginStartupData CreateDefaultStartupConfiguration() =>
        new(ConfigurationId, Guid.NewGuid().ToString(),
            JsonSerializer.Serialize(new FolderSettings(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "*", "File.Documents")), string.Empty);

    public PluginStartupData? ConfigureStartup(PluginStartupData? existing, IPluginHost host)
    {
        FolderSettings settings;
        try
        {
            settings = existing is null
                ? new FolderSettings(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "*", "File.Documents")
                : ReadSettings(existing.Value);
        }
        catch (JsonException exception)
        {
            MessageBox.Show(host.Owner, $"La configuration est invalide.\n{exception.Message}",
                Name, MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
        var values = StartupConfigurationDialog.Show(host.Owner, Name,
            [
                new("Dossier à surveiller :", settings.Path),
                new("Filtre de fichiers (ex. *.txt) :", settings.Filter),
                new("Préfixe d'événement :", settings.EventPrefix)
            ],
            ValidateSettings);
        return values is null
            ? null
            : new PluginStartupData(ConfigurationId, existing?.InstanceId ?? Guid.NewGuid().ToString(),
                JsonSerializer.Serialize(new FolderSettings(values[0], values[1], values[2])), string.Empty);
    }

    public string DescribeStartup(PluginStartupData configuration)
    {
        try
        {
            return $"{Name} — {ReadSettings(configuration.Value).Path}";
        }
        catch (JsonException)
        {
            return $"{Name} — configuration invalide";
        }
    }

    public Task StartupAsync(
        PluginStartupData configuration, IPluginHost host, CancellationToken cancellationToken)
    {
        if (configuration.ConfigurationId != ConfigurationId)
            throw new InvalidOperationException($"Configuration de démarrage inconnue : {configuration.ConfigurationId}");
        cancellationToken.ThrowIfCancellationRequested();
        var settings = ReadSettings(configuration.Value);
        if (!Directory.Exists(settings.Path))
            throw new DirectoryNotFoundException($"Le dossier à surveiller n'existe pas : {settings.Path}");

        var watcher = new FileSystemWatcher(settings.Path, settings.Filter)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
        };
        watcher.Created += (_, args) => Raise(host, settings.EventPrefix, "Created", args.FullPath);
        watcher.Changed += (_, args) => Raise(host, settings.EventPrefix, "Changed", args.FullPath);
        watcher.Deleted += (_, args) => Raise(host, settings.EventPrefix, "Deleted", args.FullPath);
        watcher.Renamed += (_, args) => Raise(
            host, settings.EventPrefix, "Renamed", $"{args.OldFullPath}\n{args.FullPath}");
        watcher.Error += (_, args) => host.Log(
            $"Erreur de surveillance du dossier {settings.Path} : {args.GetException().Message}");
        try
        {
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
        catch
        {
            watcher.Dispose();
            throw;
        }
        host.Log($"{Name} activée : {settings.Path} ({settings.Filter}).");
        return Task.CompletedTask;
    }

    public PluginActionData? ConfigureAction(string actionId, PluginActionData? existing, IPluginHost host) =>
        throw new InvalidOperationException($"{Name} ne fournit pas d'actions de macro.");

    public string DescribeAction(PluginActionData action) =>
        throw new InvalidOperationException($"{Name} ne fournit pas d'actions de macro.");

    public Task ExecuteActionAsync(PluginActionData action, IPluginHost host, CancellationToken cancellationToken) =>
        throw new InvalidOperationException($"{Name} ne fournit pas d'actions de macro.");

    public Task ResetAsync()
    {
        foreach (var watcher in _watchers)
            watcher.Dispose();
        _watchers.Clear();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await ResetAsync();

    private static void Raise(IPluginHost host, string prefix, string suffix, string payload) =>
        host.RaiseEvent($"{prefix}.{suffix}", payload);

    private static FolderSettings ReadSettings(string? value)
    {
        var settings = JsonSerializer.Deserialize<FolderSettings>(value ?? string.Empty, JsonOptions)
            ?? throw new JsonException("La configuration de surveillance de dossier est vide.");
        var error = ValidateSettings([settings.Path, settings.Filter, settings.EventPrefix]);
        if (error is not null)
            throw new JsonException(error);
        return settings;
    }

    private static string? ValidateSettings(string[] values)
    {
        if (values.Length != 3 || string.IsNullOrWhiteSpace(values[0]) || !Path.IsPathFullyQualified(values[0]))
            return "Indiquez un chemin absolu vers le dossier à surveiller.";
        var invalidFilterChars = Path.GetInvalidFileNameChars()
            .Where(character => character is not '*' and not '?').ToArray();
        if (string.IsNullOrWhiteSpace(values[1]) || values[1].IndexOfAny(invalidFilterChars) >= 0)
            return "Le filtre est vide ou invalide.";
        if (values[1].Contains(Path.DirectorySeparatorChar) || values[1].Contains(Path.AltDirectorySeparatorChar))
            return "Le filtre doit porter sur des noms de fichiers, pas sur un chemin.";
        if (string.IsNullOrWhiteSpace(values[2]) || values[2].Any(char.IsWhiteSpace))
            return "Le préfixe d'événement ne doit pas être vide ni contenir d'espace.";
        return null;
    }
}
