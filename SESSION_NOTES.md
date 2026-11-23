# Notes de reprise — EventGhostDotNet

Résumé de l'état du projet pour reprendre le travail dans VS Code ou une future session. Ce fichier décrit le code réel, pas l'historique de la conversation.

## Projet

- Application Windows WPF, cible `net9.0-windows10.0.19041.0`, `Microsoft.WindowsAppSDK` 2.7.0, WebView2, Markdig.
- Ouvrir `EventGhostDotNet.sln` (application, contrats, plugins intégrés, cœur testable, tests).
- Construire et lancer : `dotnet build EventGhostDotNet.sln`, puis `dotnet run --project EventGhostDotNet.csproj`.
- Configuration utilisateur : `%LOCALAPPDATA%\EventGhostDotNet\configuration.json`, sauvegarde précédente dans `configuration.json.bak`.

## Organisation

| Projet | Rôle |
| --- | --- |
| `EventGhostDotNet.csproj` | Fenêtre principale (partials `MainWindow.*`), modèles `Items/`, persistance, moteur de macros. |
| `Core/` | Logique pure sans WPF : formatage ANSI, migration et version de schéma, écriture atomique, manifeste et chargeur de plugins, règles d'exécution des macros, action `If`. |
| `PluginContracts/` | Contrat `IPlugin` / `IPluginHost`, cible `net9.0` **sans WPF**. |
| `Plugins/` | Plugins intégrés (Display, FileWatcher, Logitech, ProcessWatcher, Scheduler, SystemTrayMenu, Wallpaper) et `Shared/`. |
| `Tests/EventGhostDotNet.Core.Tests` | Tests xunit du projet `Core` (exécutables sans Windows). |

Services extraits de `MainWindow` :

- `ConfigurationStore` : normalisation du JSON, sérialisation, écriture atomique (`Core.Storage.AtomicFile`).
- `PluginRuntime` : chargement, verrou de cycle de vie (`EnterAsync`), démarrage, arrêt, libération des plugins.
- `ConfigurationDocument` : forme sérialisée, avec `SchemaVersion`.

## Configuration

- Le JSON contient `SchemaVersion` (constante `ConfigurationMigrator.CurrentSchemaVersion`).
- `ConfigurationMigrator.Normalize` accepte l'ancien format à racine tableau, convertit les événements `EGDN.*` en `System.*` et estampille la version avant désérialisation.
- Les migrations qui dépendent des plugins (anciennes actions `LogitechStart`, association des instances) restent dans `MainWindow.xaml.cs`.
- L'enregistrement passe par un fichier temporaire puis `File.Replace`, avec `configuration.json.bak` en secours.

## Plugins

- Le contrat ne référence plus WPF : les plugins WPF récupèrent la fenêtre hôte via `host.UserInterface.NativeOwner` (extension `OwnerWindow()` dans `Plugins/Shared/PluginHostWindows.cs`).
- `host.UserInterface` expose aussi `ShowDialog`, `ShowMessage` et `Confirm`.
- Le chargeur lit `Plugins/plugins.manifest` (une DLL par ligne, chemins relatifs). Sans manifeste, il retombe sur la convention `Plugins/<Nom>/<Nom>.dll` et les DLL à la racine de `Plugins`, sans scan récursif des dépendances.
- Le manifeste est généré par les cibles `CopyBuiltInPlugins` et `PublishBuiltInPlugins`. Un plugin ajouté à la main dans une version publiée doit être ajouté au manifeste, ou le manifeste doit être supprimé pour revenir à la convention.

## Vérifications

- `dotnet restore EventGhostDotNet.csproj -p:EnableWindowsTargeting=true` : OK.
- Sous Linux, la compilation complète échoue uniquement sur l'outil Windows `MakePri.exe` (`Exec format error`) : le C# est compilé, la génération PRI doit se faire sur Windows.
- `dotnet test Tests/EventGhostDotNet.Core.Tests/EventGhostDotNet.Core.Tests.csproj` : tests du cœur, sans WPF.
- La CI `.github/workflows/ci.yml` compile la solution et exécute les tests sur `windows-latest`.

## Points ouverts

- `MainWindow` reste volumineux (~6 000 lignes de partials) : l'orchestration des macros est encore dans la fenêtre, seules ses règles pures sont dans `Core`.
- Aucun test d'intégration WPF : les plugins et l'interface ne sont vérifiés que par compilation.
- Les DLL de plugins sont chargées dans des `AssemblyLoadContext` qui ne sont jamais déchargés.
- `ToDo.md` : minuterie multiple et niveau d'indentation initial du journal.
