# Notes de reprise — EventGhostDotNet

Ce fichier résume l'état du projet pour reprendre le travail dans VS Code ou une future session Copilot. Il ne s'agit pas d'une transcription de la conversation.

## Projet

- Dossier : `C:\Saue0\Projets\eventghostdotnet`
- Application Windows WPF, cible `net9.0-windows`.
- Construire depuis le dossier du projet avec `dotnet build`.
- Lancer avec `dotnet run`.
- Les macros sont enregistrées dans `%LOCALAPPDATA%\EventGhostDotNet\configuration.json`.

## Fonctions présentes

- Macros avec événement déclencheur et séquence d'actions.
- Actions standard, scripts PowerShell, changement d'entrée d'écran DDC/CI, contrôle HID Logitech.
- L'écoute Logitech se démarre avec l'action `LogitechStart` d'une macro; le récepteur et le préfixe des événements sont configurés sur cette action (par défaut `Logitech`).

## Équivalences des plugins étudiés

- `C:\ProgramData\EventGhost\plugins\Display`: changement d'entrée via VCP `0x60`, implémenté par les API DDC/CI Windows dans `Plugins/Display/DisplayPlugin.cs`. Le binaire tiers `ControlMyMonitor.exe` n'est pas utilisé.
- `C:\ProgramData\EventGhost\plugins\LogitechUnify`: découverte des interfaces HID C52B/C548, écoute des événements de touches et de batterie, reconnexion, changement d'hôte et envoi de rapport brut.
- Chaque action `LogitechStart` peut démarrer un récepteur distinct; les actions de commande sélectionnent le récepteur visé. La communication avec le matériel doit être validée sur les appareils visés avant usage régulier.

## Fichiers principaux

- `MainWindow.xaml` — interface.
- `MainWindow.xaml.cs` — macros, actions et intégration des modules de plugins.
- `Plugins/Display` — module Display et API Windows DDC/CI.
- `Plugins/Logitech` — module Logitech et client HID.
- `PluginContracts` et `PluginLoader.cs` — contrat et découverte des DLL de plugins.
- `Models.cs` — modèle sérialisé des macros/actions.
- `README.md` — démarrage et résumé des capacités.

Chaque plugin ajouté est une DLL de confiance implémentant `IPlugin` et placée dans un sous-dossier de `Plugins` à côté de l'exécutable.

Les événements de cycle de vie `EGDN.ConfigurationLoaded` et `EGDN.Startup` sont déclenchés après le chargement initial. Le menu Fichier permet de recharger le JSON; cette opération annule les macros en cours, libère les clients Logitech et remplace les macros en mémoire avant `EGDN.ConfigurationLoaded`.

## Dernière validation

- `dotnet build --no-restore` a réussi sans avertissements ni erreurs.
- Test local : 3 écrans DDC/CI et 2 récepteurs Logitech détectés; ouverture puis fermeture d'un handle HID réussie.
- Aucun changement d'entrée d'écran ni rapport HID de commande n'a été envoyé pendant le test.
