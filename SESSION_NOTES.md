# Notes de reprise — EventGhostDotNet

Résumé de l'état du projet pour reprendre le travail dans VS Code ou une future session. Ce fichier décrit le code réel, pas l'historique de la conversation.

## Projet

- Application Windows WPF, cible `net9.0-windows10.0.19041.0`, Markdig. Les notifications sont des fenêtres WPF (`NotificationService` / `NotificationWindow`) et la documentation Markdown est rendue en `FlowDocument` (`MarkdownFlowDocumentRenderer`) : ni Windows App SDK ni WebView2 ne sont référencés.
- Ouvrir `EventGhostDotNet.sln` (application, contrats, plugins intégrés, cœur testable, tests).
- Construire et lancer : `dotnet build EventGhostDotNet.sln`, puis `dotnet run --project EventGhostDotNet.csproj`.
- Configuration utilisateur : `%LOCALAPPDATA%\EventGhostDotNet\configuration.json`, sauvegarde précédente dans `configuration.json.bak`.
- Instance unique par session (`SingleInstanceGuard` : mutex `Local\EventGhostDotNet.Instance` + pipe nommé) ; un second lancement réactive la fenêtre existante.
- Erreurs inattendues : gestionnaires globaux dans `App` et journal `%LOCALAPPDATA%\EventGhostDotNet\errors.log` (rotation en `.old` à 1 Mo).

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
- `ConfigurationDocument` : forme sérialisée, avec `SchemaVersion` et `Display` (`DisplaySettings` : barre d'outils, développement automatique, heure, horodatage, indentation, niveaux et catégories du journal). `ApplyDisplaySettings` les applique au chargement et au rechargement, et `PersistDisplaySettings` les réenregistre à chaque changement.
- `PowerShellScriptEditor` : éditeur de script AvalonEdit colorisé par la grammaire TextMate PowerShell (`TextMateSharp`, thème clair), avec import des jetons/états de fin de ligne mis en cache ; repli en texte brut si la grammaire ne charge pas.
- Emoji : WPF ne rend pas les polices couleur, les emoji sortiraient en noir et blanc. `Emoji.Wpf` dessine les glyphes COLR/CPAL de Segoe UI Emoji en vectoriel ; l'éditeur remplace chaque grappe emoji par un `InlineObjectElement` (texte du document inchangé), et `NotificationWindow` / `NotificationHistoryWindow` utilisent `Emoji.Wpf.TextBlock`.
- Journal (`WriteLog` / `EventLogEntry`) : indentation de 2 espaces par niveau, 64 niveaux maximum, activée par « Indenter le journal ». `DispatchEventAsync` journalise chaque événement au niveau zéro, y compris lorsqu'une action l'émet, puis exécute les macros correspondantes au niveau 1 : l'indentation ne s'accumule plus d'un événement à l'autre. Le payload d'un événement reçu est conservé dans l'entrée (`EventLogEntry.Payload`) et « Rejouer l'événement » le redistribue tel quel.
- `NotificationService` / `NotificationWindow` / `NotificationHistoryWindow` : notifications empilées en bas à droite de l'écran principal (zone de travail `GetMonitorInfo` convertie en DIP avec le DPI de la fenêtre, repli `SystemParameters.WorkArea`), recalculées sur `DisplaySettingsChanged` et `DpiChanged`, remplacement par clé, clic pour afficher EventGhost, historique de 50 entrées cliquable depuis le menu de l'icône. `NotificationHistoryEntry.Key` permet à une notification de même clé d'écraser son entrée dans l'historique ; `NotificationService.Remove` supprime une entrée (bouton ✕ de `NotificationHistoryWindow`, la fenêtre s'ajuste entre 220 et 460 px). Les bulles retirent les espaces de fin, masquent un titre ou un message vide et plafonnent le message à 140 px avec `CharacterEllipsis`. `NotificationWindow` n'utilise pas `SizeToContent` : une fenêtre transparente y est mal dimensionnée par WPF (double prise en compte du DPI), la hauteur est donc mesurée sur le contenu (`FitToContent`).

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
- `Scheduler` : plusieurs tâches temporaires peuvent être actives simultanément ; `_jobs` associe chaque identifiant à la liste de ses exécutions en cours, donc deux minuteries de même nom tournent en parallèle et « Annuler une tâche planifiée » les arrête toutes.
- Les capacités dépendantes de Windows sont exposées par `IPluginHost.IdleTime`, `Monitors` et `HidDevices` (interfaces dans `PluginContracts/PlatformServices.cs`, `null` si indisponibles). Les implémentations Win32 sont regroupées dans `Platform/Windows/` ; la zone de notification de l'application passe par `ITrayIconService` (`Platform/Windows/WindowsTrayIconService.cs`). `host.Session` expose le verrouillage et la veille : l'inactivité est suspendue et repart au déverrouillage, et Logitech ferme puis rouvre la connexion HID à la reprise. L'inactivité est suivie en 64 bits (`TickCount64`), y compris au-delà du cycle de 49,7 jours de `GetLastInputInfo` ; un échec de cette API est journalisé une seule fois dans `errors.log` et ne déclenche plus de planification. Les listes d'écrans et d'interfaces HID sont mises en cache : `SystemEvents.DisplaySettingsChanged` vide le cache des moniteurs, et une fenêtre de messages `WM_DEVICECHANGE` (`RegisterDeviceNotification`, GUID HID) vide celui du HID et notifie `IHidDeviceService.DevicesChanged` ; Logitech réessaie alors immédiatement au lieu d'attendre 2 s. L'icône de notification (`WindowsTrayIconService`) recharge sa frame à la taille système au changement d'affichage, se réaffiche sur `TaskbarCreated` après un redémarrage de l'Explorateur, ouvre la fenêtre au clic gauche et borne l'info-bulle à 63 caractères ; `Assets/EventGhostDotNet.ico` contient les tailles 16, 20, 24, 32, 48 et 256.

## Vérifications

- `dotnet restore EventGhostDotNet.csproj -p:EnableWindowsTargeting=true` : OK.
- Sous Linux, `dotnet build EventGhostDotNet.csproj -p:EnableWindowsTargeting=true` et `dotnet build EventGhostDotNet.sln -p:EnableWindowsTargeting=true` réussissent entièrement depuis la suppression de Windows App SDK (plus de `MakePri.exe` ni de génération PRI).
- `dotnet test Tests/EventGhostDotNet.Core.Tests/EventGhostDotNet.Core.Tests.csproj` : tests du cœur, sans WPF.
- La CI `.github/workflows/ci.yml` compile la solution et exécute les tests sur `windows-latest`.

## Points ouverts

- `MainWindow` reste volumineux (~6 000 lignes de partials) : l'orchestration des macros est encore dans la fenêtre, seules ses règles pures sont dans `Core`.
- Aucun test d'intégration WPF : les plugins et l'interface ne sont vérifiés que par compilation.
- Les DLL de plugins sont chargées dans des `AssemblyLoadContext` qui ne sont jamais déchargés.
