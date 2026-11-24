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
- [x] **Rejouer un Event du journal** — le payload d'un événement reçu est conservé dans sa ligne de journal ; le bouton « Rejouer l'événement » relance la distribution de cet événement avec son payload d'origine et réexécute les macros correspondantes. *(MainWindow.xaml, MainWindow.MacroEvent.cs, MainWindow.xaml.cs — S)*

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
