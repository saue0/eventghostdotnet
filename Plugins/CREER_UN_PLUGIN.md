# Créer un plugin

L'application charge les plugins au démarrage depuis les DLL placées dans le sous-dossier `Plugins` à côté de son exécutable. Ce guide décrit la création d'un plugin externe et son intégration comme plugin intégré. Pour suivre les exemples et construire un plugin, utilisez une copie du dépôt source avec le SDK .NET 9 ou plus récent; la version publiée ne contient pas les projets ni leur code source.

## 1. Créer le projet

Pour un développement dans ce dépôt, créez `Plugins\MonPlugin\MonPlugin.csproj` et ajoutez une référence à `PluginContracts` :

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\PluginContracts\PluginContracts.csproj" />
  </ItemGroup>
</Project>
```

Si le projet est en dehors du dépôt, adaptez le chemin de la référence à `PluginContracts.csproj`. `UseWPF` est nécessaire pour référencer le contrat qui expose `IPluginHost.Owner`.

## 2. Implémenter `IPlugin`

Voici un plugin minimal qui ajoute une action, l'affiche dans le journal et émet un événement :

```csharp
using EventGhostDotNet.PluginContracts;

namespace MonPlugin;

public sealed class MonPluginModule : IPlugin
{
    private const string WriteMessageAction = "WriteMessage";

    public string Id => "MonPlugin";
    public string Name => "Mon plugin";
    public IReadOnlyList<PluginActionDescriptor> Actions { get; } =
        [new(WriteMessageAction, "Écrire un message")];

    public PluginActionData? ConfigureAction(
        string actionId, PluginActionData? existing, IPluginHost host)
    {
        if (actionId != WriteMessageAction)
            throw new InvalidOperationException($"Action inconnue : {actionId}");

        return new PluginActionData(
            actionId,
            existing?.Value ?? "Bonjour",
            existing?.Target ?? string.Empty);
    }

    public string DescribeAction(PluginActionData action) =>
        $"Écrire : {action.Value}";

    public Task ExecuteActionAsync(
        PluginActionData action, IPluginHost host, CancellationToken cancellationToken)
    {
        if (action.ActionId != WriteMessageAction)
            throw new InvalidOperationException($"Action inconnue : {action.ActionId}");

        cancellationToken.ThrowIfCancellationRequested();
        host.Log(action.Value);
        host.Result = action.Value;
        host.RaiseEvent("MonPlugin.MessageWritten", action.Value);
        return Task.CompletedTask;
    }

    public Task ResetAsync() => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

`ConfigureAction` reçoit `existing == null` lors de la création et la configuration existante lors d'une modification. Elle retourne `null` si l'utilisateur annule. `PluginActionData.Value` et `Target` sont des chaînes sérialisées avec l'action; elles peuvent contenir du JSON si plusieurs paramètres sont nécessaires.

Chaque plugin doit avoir un `Id` stable, non vide et unique. Les identifiants d'actions doivent aussi être non vides et uniques dans le plugin. Le chargeur crée chaque classe publique, concrète et implémentant `IPlugin` avec son constructeur sans paramètre; une DLL peut donc contenir plusieurs plugins, à condition que leurs identifiants soient distincts.

## 3. Utiliser le contexte hôte

Les actions reçoivent un `IPluginHost` et un jeton d'annulation :

- `host.Log(message)` ajoute un message de niveau Information au journal. `host.Log(message, LogLevel.Warning)` permet de préciser le niveau (`Debug`, `Information`, `Warning` ou `Error`).
- `host.Result` transmet une valeur à l'action suivante de la macro.
- `host.RaiseEvent(name, payload)` émet un événement que les macros peuvent écouter.
- `host.GetMacros()` retourne les identifiants et noms des macros configurées ; `host.RunMacroAsync(id, cancellationToken)` exécute une macro active par son identifiant et signale une cible absente ou désactivée.
- `host.Owner` fournit la fenêtre WPF propriétaire d'un formulaire de configuration.

Les actions longues devraient être asynchrones et vérifier `cancellationToken`. Les macros peuvent s'exécuter simultanément : synchronisez tout état partagé par les actions du plugin. En cas d'échec, signalez-le par une exception plutôt que de retourner une réussite factice.

## 4. Ajouter une configuration de démarrage (facultatif)

Un plugin qui doit écouter un périphérique ou exécuter une tâche de fond peut fournir un démarrage :

1. Redéfinissez `HasStartupConfiguration` pour retourner `true`.
2. Implémentez `CreateDefaultStartupConfiguration`, `ConfigureStartup` et `DescribeStartup`.
3. Lancez la ressource dans `StartupAsync`, en tenant compte du jeton d'annulation.
4. Arrêtez toutes les tâches et libérez toutes les ressources dans `ResetAsync`.

`StartupAsync` peut être appelé plusieurs fois sur la même instance de plugin, une fois par configuration active. Gérez séparément ces instances si nécessaire. `ResetAsync` est appelé avant un rechargement ou un redémarrage des plugins; `DisposeAsync` est appelé à la fermeture de l'application.

`IPluginInstanceProvider` est facultatif. Implémentez-le si les actions doivent être liées à une configuration de démarrage précise, comme des actions qui ciblent un récepteur particulier.

## 5. Construire et installer un plugin externe

Construisez le projet :

```powershell
dotnet build .\Plugins\MonPlugin\MonPlugin.csproj -c Release
```

Copiez le fichier `MonPlugin.dll`, son fichier `MonPlugin.deps.json` et les DLL de ses dépendances dans un sous-dossier distinct du dossier `Plugins` de l'application, par exemple :

```text
EventGhostDotNet.exe
Plugins\
  MonPlugin\
    MonPlugin.dll
    MonPlugin.deps.json
    DependancesDuPlugin.dll
```

Le chargeur recherche les DLL dans les sous-dossiers de `Plugins` au démarrage. Il utilise le fichier `.deps.json` pour résoudre les dépendances du plugin; le contrat `PluginContracts` est fourni par l'application et ne doit pas être copié comme une dépendance distincte. Placez un fichier `README.md` à côté de la DLL pour le rendre accessible depuis le menu Aide. Redémarrez EventGhost .NET pour charger le nouveau plugin. Le journal indique les plugins chargés ou les erreurs de chargement.

## 6. Intégrer le plugin au dépôt

Pour qu'un plugin soit construit et publié avec l'application :

1. Ajoutez son projet à `EventGhostDotNet.csproj` comme les projets des plugins intégrés.
2. Ajoutez-le à la liste passée à la cible `CopyBuiltInPlugins` pour copier sa DLL et son `.deps.json` dans `bin\...\Plugins\MonPlugin`.
3. Ajoutez les fichiers correspondants à la cible `PublishBuiltInPlugins`.
4. Documentez ses fonctionnalités dans `Plugins\MonPlugin\README.md`. Les README de plugins et ce guide sont copiés lors de la compilation et de la publication.

Les plugins sont chargés avec les droits de l'application. Installez uniquement des DLL de confiance.

## Exemples dans le dépôt

- Dans le dépôt source, `Plugins\Display\DisplayModule.cs` montre une action configurée avec un formulaire WPF.
- `Plugins\SystemTrayMenu\SystemTrayMenuModule.cs` montre un plugin sans action de macro qui fournit une configuration de démarrage.
- `Plugins\Logitech\LogitechModule.cs` montre l'association d'actions à plusieurs configurations de démarrage avec `IPluginInstanceProvider`.
