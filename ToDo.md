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

- [ ] **Minuterie multiple** — supporter plus d'un seul timer dans le plugin scheduler.
- [ ] **LOG indentation** — Tous les events parte au niveau zéro pour l'indentation.
- [ ] **Historique des Notification** — Une notification avec la meme clé écrase la précedente, avoir la possibilité de retirer une notification de l'historique, ajuster la hauteur de la notification au minimum possible et avoir un max raisonnable

## Priorité 3 — intégration Windows

- [ ] **Démarrage avec Windows** — case dans les préférences, clé `HKCU\...\CurrentVersion\Run` avec `--minimized`, et analyse des arguments au démarrage. *(App.xaml.cs, MainWindow — S/M)*
- [ ] **Lecture DDC/CI** — `GetVCPFeature` pour afficher l'entrée courante, actions luminosité/contraste et reprise après réveil du moniteur. *(WindowsMonitorService.cs, DisplayModule — M)*
- [ ] **Thème sombre et contraste élevé** — remplacer les couleurs figées de `App.xaml` et du renderer Markdown par des pinceaux système et des variantes sombres. *(App.xaml, MarkdownFlowDocumentRenderer.cs — M)*
- [ ] **Manifeste d'application** — `PerMonitorV2`, `longPathAware`, `supportedOS` et `asInvoker` ; vérifier à 150/200 % sur deux écrans. *(nouveau app.manifest — S)*
- [ ] **Sécurité des plugins** — avertissement de confiance, empreinte dans `plugins.manifest` et déchargement des `AssemblyLoadContext` au rechargement. *(Core/Plugins, PluginRuntime — M)*
- [ ] **PowerShell** — détecter `pwsh.exe`, proposer un chemin configurable et repli sur Windows PowerShell 5.1 avec avertissement. *(MainWindow.Macro.cs — S/M)*

## Priorité 4 — distribution et qualité

- [ ] **Empaquetage** — MSIX ou installateur, signature de code, mise à jour intégrée, build ARM64 et option autonome sans .NET 9 Desktop Runtime. *(csproj, .vscode — L)*
- [ ] **CI Windows** — `dotnet publish`, démarrage smoke de l'exécutable et test de chargement des 7 plugins, en plus du build et des tests `Core`. *(.github/workflows/ci.yml — M)*
- [ ] **Diagnostic** — journal fichier rotatif, « Ouvrir le dossier des journaux » et entrée Event Log en cas d'exception fatale. *(MainWindow — S/M)*
- [ ] **Accessibilité** — noms UI Automation sur l'arborescence et le journal, navigation clavier du menu de l'icône. *(MainWindow.xaml — M)*

## Priorité 5 — Refactor de fond

- [ ] **Extraire le moteur de macros** — sortir l'orchestration encore dans `MainWindow` (~6 000 lignes de partials) vers un service testable ; seules les règles pures sont dans `Core`.
