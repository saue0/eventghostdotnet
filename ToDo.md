# Todo

## Priorité 1 — robustesse Windows

- [x] **Instance unique** — mutex `Local\EventGhostDotNet.Instance` et réactivation de la première instance par pipe nommé ; aujourd'hui deux lancements créent deux icônes de notification et deux écritures concurrentes de `configuration.json`. *(App.xaml.cs, MainWindow.xaml.cs — M)*
- [x] **Exceptions non gérées** — `DispatcherUnhandledException` et `AppDomain.UnhandledException`, journal fichier et dialogue de secours. *(App.xaml.cs — S)*
- [x] **Verrouillage de session et veille** — écouter `SystemEvents.SessionSwitch` et `PowerModeChanged` ; ne pas déclencher l'inactivité après déverrouillage ou reprise, et rouvrir les périphériques HID. *(Platform/Windows, Logitech — S/M)*
- [x] **Inactivité fiable** — remplacer `Environment.TickCount` (32 bits) par `TickCount64`, journaliser l'erreur une seule fois et ignorer l'inactivité pendant le verrouillage. *(WindowsIdleTimeProvider.cs — S)*
- [x] **Changements d'affichage et de périphériques** — `DisplaySettingsChanged` et `WM_DEVICECHANGE`/`RegisterDeviceNotification` au lieu de réénumérer à chaque dialogue ou toutes les 2 s. *(WindowsMonitorService.cs, WindowsHidDeviceService.cs — M)*
- [x] **Icône de notification** — redémarrage de l'Explorateur, icône multi-tailles (16/20/24/32/48), clic gauche = afficher, info-bulle limitée à 63 caractères. *(WindowsTrayIconService.cs — S/M)*
- [x] **Notifications** — positionnement sur l'écran principal, conversion DPI et repositionnement au changement d'affichage, historique des 50 dernières notifications cliquable depuis le menu de l'icône ; le centre de notifications Windows n'est pas utilisé. *(NotificationService.cs, NotificationHistoryWindow.xaml — M)*

## Priorité 2 — Backlog existant

- [x] **Minuterie multiple** — plusieurs minuteries peuvent être actives en même temps dans le plugin scheduler, y compris sous le même identifiant : elles s'exécutent en parallèle et « Annuler une tâche planifiée » les arrête toutes. *(SchedulerModule.cs — S)*
- [x] **LOG indentation** — tous les événements partent au niveau zéro : un événement émis depuis une action n'hérite plus de l'indentation de cette action, sa ligne est une racine du journal et la macro déclenchée démarre au niveau 1. *(MainWindow.MacroEvent.cs — S)*
- [x] **Historique des Notification** — une notification de même clé écrase son entrée dans l'historique (et non seulement la bulle affichée), chaque entrée peut être retirée avec ✕, et la fenêtre s'ajuste à son contenu entre 220 et 460 px ; les bulles masquent un titre ou un message vide, retirent les espaces de fin et plafonnent le message à 140 px avec une ellipse. *(NotificationService.cs, NotificationHistoryWindow.xaml, NotificationWindow.xaml.cs — S/M)*
- [x] **Conservation des options d'affichage** — barre d'outils, développement automatique de l'arbre, heure, horodatage, indentation et filtres du journal (niveaux et catégories) sont enregistrés dans `configuration.json` dès qu'ils changent et restaurés au démarrage. *(ConfigurationDocument.cs, MainWindow.xaml.cs, MainWindow.Menus.cs — S)* 
- [x] **Rejouer un Event du journal** — le payload et la source (`SourceId`) d'un événement reçu sont conservés dans sa ligne de journal ; le bouton « Rejouer l'événement » relance la distribution de cet événement avec son payload et sa source d'origine et réexécute les macros correspondantes. *(MainWindow.xaml, MainWindow.MacroEvent.cs, MainWindow.xaml.cs — S)*

## Priorité 3 — intégration Windows

- [x] **Démarrage avec Windows** — case dans les préférences, clé `HKCU\...\CurrentVersion\Run` avec `--minimized`, et analyse des arguments au démarrage : la valeur `EventGhostDotNet` contient la ligne de commande citée, et l'argument force un démarrage minimisé dans la zone de notification. *(WindowsStartupRegistration.cs, App.xaml.cs, MainWindow.xaml.cs — S)*
- [x] **Lecture DDC/CI** — `GetVCPFeature` lit l'entrée courante (affichée dans le dialogue et renvoyée par l'action « Écran — lire l'entrée courante »), les actions luminosité (`0x10`) et contraste (`0x12`) convertissent un pourcentage sur la valeur maximale du moniteur, et les derniers réglages sont réappliqués après réveil (2 s de délai). *(WindowsMonitorService.cs, DisplayModule.cs, PlatformServices.cs — M)*
- [x] **Thème sombre et contraste élevé** — les styles de `App.xaml` ne contiennent plus de couleurs figées : elles sont pilotées par des ressources dynamiques que `Theme/AppTheme.cs` remplace au démarrage selon `AppsUseLightTheme` et `SystemParameters.HighContrast`, puis réapplique sur `UserPreferenceChanged` et au changement de contraste. Le renderer Markdown, les notifications, la fenêtre principale et ses icônes d'arbre suivent ces pinceaux ; en contraste élevé, seules les couleurs système sont utilisées et le survol ne change que la bordure. *(App.xaml, Theme/AppTheme.cs, MarkdownFlowDocumentRenderer.cs, MainWindow.xaml — M)*
- [x] **Manifeste d'application** — `app.manifest` embarqué dans l'exécutable déclare `PerMonitorV2,PerMonitor` (repli `dpiAware true/pm`), `longPathAware`, `supportedOS` Windows 10/11 et `asInvoker` ; les plugins chargés dans le processus héritent de la conscience DPI. La vérification visuelle à 150/200 % sur deux écrans reste à faire sous Windows. *(app.manifest, EventGhostDotNet.csproj — S)*
- [x] **Sécurité des plugins** — `plugins.manifest` associe chaque DLL à une empreinte SHA-256 générée par les cibles `CopyBuiltInPlugins`/`PublishBuiltInPlugins`. Le chargeur refuse une empreinte différente (journal Erreur) et demande une approbation explicite pour une DLL sans empreinte (avertissement de confiance, refus par défaut). « Fichier > Recharger les plugins » libère les plugins, décharge les `AssemblyLoadContext` (avertissement si un contexte survit) puis recharge et redémarre les configurations. *(Core/Plugins, PluginLoader.cs, PluginRuntime.cs, MainWindow.xaml — M)*
- [x] **PowerShell** — `PowerShellExecutables` cherche `pwsh.exe` dans un chemin configuré, le `PATH` puis les emplacements d'installation connus. Le chemin se règle dans Fichier > Préférences PowerShell et se conserve dans `PowerShellPath` de `configuration.json` (vide = détection automatique). Sans PowerShell 7, le script s'exécute avec Windows PowerShell 5.1 et un avertissement est journalisé une fois par session ; la case par action force toujours l'ancien moteur. *(Core/PowerShell, MainWindow.xaml.cs, MainWindow.MacroAction.cs — M)*

## Priorité 4 — distribution et qualité

- [ ] **Empaquetage** — MSIX ou installateur, signature de code, mise à jour intégrée, build ARM64 et option autonome sans .NET 9 Desktop Runtime. *(csproj, .vscode — L)*
- [ ] **CI Windows** — `dotnet publish`, démarrage smoke de l'exécutable et test de chargement des 7 plugins, en plus du build et des tests `Core`. *(.github/workflows/ci.yml — M)*
- [ ] **Diagnostic** — journal fichier rotatif, « Ouvrir le dossier des journaux » et entrée Event Log en cas d'exception fatale. *(MainWindow — S/M)*
- [ ] **Accessibilité** — noms UI Automation sur l'arborescence et le journal, navigation clavier du menu de l'icône. *(MainWindow.xaml — M)*

## Priorité 5 — Refactor de fond

- [ ] **Extraire le moteur de macros** — sortir l'orchestration encore dans `MainWindow` (~6 000 lignes de partials) vers un service testable ; seules les règles pures sont dans `Core`.
