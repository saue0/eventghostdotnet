# EventGhost .NET

Application de bureau Windows en WPF et .NET 9, inspirée du fonctionnement d'EventGhost. Cette implémentation est indépendante et ne reprend pas son code source.

## Démarrer

Depuis ce dossier, avec le SDK .NET 9 ou plus récent :

```powershell
dotnet run
```

## Fonctions disponibles

- Créer, modifier et supprimer des macros avec un ou plusieurs événements déclencheurs ; la réception de l'un d'eux exécute la macro.
- Modifier une macro ou une action par double-clic ou avec son crayon ; la corbeille demande confirmation avant suppression.
- Associer, modifier et supprimer une séquence d'actions : écrire dans le journal, attendre, émettre un signal sonore, afficher un message, ouvrir une URL HTTP(S) ou exécuter un script PowerShell.
- Activer ou désactiver une macro ou une action avec sa case à cocher ou une action système ciblée. Une macro inactive ne réagit plus aux événements, mais reste exécutable manuellement ; ses actions inactives sont ignorées.
- Changer l'entrée d'un écran compatible DDC/CI via son code VCP 0x60.
- Écouter les rapports HID d'un ou plusieurs récepteurs Logitech Unifying ou Bolt, déclencher des événements de touches et de batterie, changer d'hôte et envoyer des rapports bruts.
- Exécuter une macro ou déclencher manuellement un événement exact pour exécuter les macros correspondantes.
- Sélectionner un événement reçu dans le journal pour copier son nom ou l'ajouter aux déclencheurs de la macro sélectionnée ; enregistrer la configuration pour conserver la modification.
- Déclencher `EGDN.Startup` au démarrage et `EGDN.ConfigurationLoaded` après le chargement ou le rechargement de la configuration.
- Recharger manuellement la configuration depuis le menu Fichier; les macros en cours sont annulées et les écoutes Logitech de l'ancienne configuration sont libérées avant le remplacement.
- Enregistrer et recharger les macros en JSON dans `%LOCALAPPDATA%\EventGhostDotNet\configuration.json`.
- À la fermeture, choisir d'enregistrer ou d'abandonner les modifications non enregistrées, ou d'annuler la fermeture.

Dans le fichier JSON, chaque macro contient une liste `EventNames`, par exemple `"EventNames": ["Unify.Host 1", "Unify.Host 2"]`. Les anciens champs `EventName` et `AdditionalEventNames` ne sont plus pris en charge.

Les actions système peuvent activer ou désactiver une macro ou une action d'une macro, même après renommage. Une macro déjà en cours continue son exécution lorsqu'elle est désactivée. Désactiver une action ne l'interrompt pas si elle est déjà en cours et n'arrête pas les ressources qu'elle a déjà démarrées. Les changements d'état prennent effet immédiatement, sans sauvegarde automatique ; ils sont conservés si la configuration est ensuite enregistrée. Un changement d'état effectué par une action système ne déclenche pas à lui seul l'avertissement de fermeture, contrairement à un changement manuel. Par défaut, les macros et actions sont actives.

## Modules de plugins

L'application découvre les DLL dans le dossier `Plugins` à côté de son exécutable au démarrage. Display et Logitech sont compilés et copiés dans ce dossier automatiquement. Pour ajouter un plugin externe, créer une bibliothèque .NET 9 Windows qui référence le projet `PluginContracts` et expose une classe publique sans paramètre implémentant `IPlugin`; placer sa DLL, son fichier `.deps.json` et ses dépendances dans un sous-dossier distinct de `Plugins`, puis redémarrer l'application.

Le plugin fournit un identifiant stable, ses actions et leur formulaire, et exécute ses actions via `IPlugin`. Les valeurs des actions sont enregistrées avec les identifiants du plugin et de l'action. `ResetAsync` doit arrêter et libérer les ressources créées par le plugin : il est appelé lors du rechargement de configuration et de l'arrêt des plugins. `DisposeAsync` est appelé à la fermeture de l'application. Les anciennes actions Display et Logitech sont reconnues par leurs identifiants existants.

Un plugin proposant plusieurs configurations peut aussi implémenter `IPluginInstanceProvider`. Il indique les actions de configuration et celles qui en dépendent, fournit leur nom, associe les anciennes actions à une configuration et lie les actions à la configuration actuelle. L'application conserve un identifiant stable pour chaque configuration et propose une entrée par configuration dans la liste des actions. Les plugins qui ne l'implémentent pas conservent leur fonctionnement habituel.

Les plugins s'exécutent avec les droits de l'application : installer uniquement des DLL de confiance.

Les scripts PowerShell s'exécutent sans profil, sans interaction, avec les droits de l'application et une limite de 30 secondes. Leur sortie et leurs erreurs sont consignées dans le journal. N'exécutez que des scripts de confiance.

PowerShell est le choix recommandé pour l'automatisation Windows et l'accès aux outils système. Les événements génériques peuvent aussi être déclenchés manuellement; les autres plugins et pilotes d'EventGhost ne sont pas inclus.

Les actions écran utilisent les API DDC/CI de Windows et nécessitent un moniteur compatible. Choisissez le code d'entrée VCP documenté par le fabricant. Pour Logitech, ajoutez une action « Logitech — démarrer l'écoute » par récepteur dans une macro. Chaque action choisit un récepteur Unifying (C52B) ou Bolt (C548) compatible et un préfixe d'événements distinct. Les actions de changement d'hôte et d'envoi HID apparaissent ensuite une fois par configuration, identifiées par leur préfixe. Si le récepteur d'une configuration change, ses actions suivent la nouvelle configuration ; l'écoute doit être démarrée avant leur exécution. Si une configuration est supprimée, ses actions associées ne peuvent plus être exécutées.
