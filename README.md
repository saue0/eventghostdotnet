# EventGhost .NET

Application de bureau Windows en WPF et .NET 9, inspirée du fonctionnement d'EventGhost. Cette implémentation est indépendante et ne reprend pas son code source.

## Démarrer

Depuis ce dossier, avec le SDK .NET 9 ou plus récent :

```powershell
dotnet run
```

## Fonctions disponibles

- Consulter la configuration en arborescence : chaque macro contient ses événements déclencheurs et sa séquence d'actions. Le journal est affiché à côté et la séparation est redimensionnable.
- Gérer les démarrages de plugins et une séquence d'actions exécutées après leur initialisation, dans une section dédiée en tête de l'arborescence. Les actions de démarrage sont lancées au démarrage de l'application et au rechargement de la configuration.
- Sélectionner une macro ou l'un de ses éléments pour agir sur cette macro ; sélectionner une action pour la modifier ou la supprimer.
- Modifier un événement individuellement par double-clic ou avec son crayon, ou le supprimer avec sa corbeille. Une macro peut ne contenir aucun événement et être lancée manuellement.
- Activer ou désactiver chaque événement avec sa case à cocher ; un événement désactivé reste dans la configuration, mais ne déclenche pas la macro.
- Créer, modifier et supprimer des macros avec zéro ou plusieurs événements déclencheurs ; la réception de l'un d'eux exécute la macro.
- Modifier une macro ou une action par double-clic ou avec son crayon ; la corbeille demande confirmation avant suppression.
- Associer, modifier et supprimer une séquence d'actions : écrire dans le journal, attendre, émettre un signal sonore, afficher un message, ouvrir une URL HTTP(S), déclencher un événement avec payload facultatif ou exécuter un script PowerShell.
- Choisir une action dans un sélecteur regroupé par source : Système, Logitech ou Display.
- Fournir un contexte à chaque action et plugin : `Log` écrit dans le journal, `Result` transmet une valeur à l'action suivante et `RaiseEvent` déclenche un événement.
- Ajouter des branchements système : `Jump` peut appeler une autre macro en arrêtant ou reprenant la macro courante, ou sauter vers une action de celle-ci ; `If` compare le résultat de l'action précédente à une chaîne (égalité, présence ou différence) et saute l'action suivante si la condition est fausse.
- Activer ou désactiver une macro ou une action avec sa case à cocher ou une action système ciblée. Une macro inactive ne réagit plus aux événements, mais reste exécutable manuellement ; ses actions inactives sont ignorées.
- Changer l'entrée d'un écran compatible DDC/CI via son code VCP 0x60.
- Écouter les rapports HID d'un ou plusieurs récepteurs Logitech Unifying ou Bolt, déclencher des événements de touches et de batterie, changer d'hôte et envoyer des rapports bruts.
- Configurer un menu dans la zone de notification Windows avec des éléments et séparateurs ; par défaut, il propose Afficher EventGhost, Masquer EventGhost et Sortie. Ces choix déclenchent `System.Show`, `System.Hide` et `System.Exit`, traités par l'application.
- Exécuter une macro ou déclencher manuellement un événement exact pour exécuter les macros correspondantes.
- Exécuter l'élément sélectionné : une macro complète ou une action seule.
- Sélectionner un événement reçu dans le journal pour copier son nom ou l'ajouter aux déclencheurs de la macro sélectionnée ; enregistrer la configuration pour conserver la modification.
- Déclencher `System.Startup` au démarrage et `System.ConfigurationLoaded` après le chargement ou le rechargement de la configuration.
- Recharger manuellement la configuration depuis le menu Fichier; les macros en cours sont annulées et les écoutes Logitech de l'ancienne configuration sont libérées avant le remplacement.
- Enregistrer et recharger les démarrages de plugins et les macros en JSON dans `%LOCALAPPDATA%\EventGhostDotNet\configuration.json`. Les anciens fichiers contenant directement un tableau de macros restent lisibles.
- À la fermeture, choisir d'enregistrer ou d'abandonner les modifications non enregistrées, ou d'annuler la fermeture.

Le nouveau document JSON contient `PluginStartups`, `StartupActions` et `Macros`; chaque macro contient une liste `EventNames`, par exemple `"EventNames": ["Unify.Host 1", "Unify.Host 2"]`. Les anciens fichiers où la racine est directement le tableau des macros sont lus et convertis. Les anciens événements système préfixés par `EGDN` sont convertis vers `System.*` au chargement. Les anciens champs `EventName` et `AdditionalEventNames` ne sont plus pris en charge.

Les actions système peuvent activer ou désactiver une macro ou une action d'une macro, même après renommage. Une macro déjà en cours continue son exécution lorsqu'elle est désactivée. Désactiver une action ne l'interrompt pas si elle est déjà en cours et n'arrête pas les ressources qu'elle a déjà démarrées. Les changements d'état prennent effet immédiatement, sans sauvegarde automatique ; ils sont conservés si la configuration est ensuite enregistrée. Un changement d'état effectué par une action système ne déclenche pas à lui seul l'avertissement de fermeture, contrairement à un changement manuel. Par défaut, les macros et actions sont actives.

Les comparaisons de `If` ne tiennent pas compte de la casse et utilisent le résultat de l'action précédente ; une action désactivée, absente ou en erreur n'a pas de résultat. Chaque action commence avec sa valeur configurée comme résultat par défaut. Un script PowerShell peut utiliser `$eg.Log("message")`, `$eg.Result = "OK"` (ou `$eg.SetResult("OK")`) et `$eg.RaiseEvent("Nom", "contenu")` ; sa sortie standard est aussi envoyée au journal. Les appels récursifs de macros et les boucles de saut sont bornés pour éviter une exécution sans fin.

Les plugins peuvent lire ou modifier `host.Result` et utiliser `host.Log(...)` ou `host.RaiseEvent(...)` pendant `ExecuteActionAsync`.

## Modules de plugins

L'application découvre les DLL dans le dossier `Plugins` à côté de son exécutable au démarrage. Display, Logitech et System Tray Menu sont compilés et copiés dans ce dossier automatiquement. Pour ajouter un plugin externe, créer une bibliothèque .NET 9 Windows qui référence le projet `PluginContracts` et expose une classe publique sans paramètre implémentant `IPlugin`; placer sa DLL, son fichier `.deps.json` et ses dépendances dans un sous-dossier distinct de `Plugins`, puis redémarrer l'application.

Le plugin fournit un identifiant stable, ses actions et leur formulaire, et exécute ses actions via `IPlugin`. Il peut également fournir des configurations de démarrage et les exécuter avec `StartupAsync`; les plugins sans initialisation spécifique héritent d'un démarrage sans effet. `ResetAsync` doit arrêter et libérer les ressources créées par le plugin : il est appelé lors du rechargement de configuration et avant le redémarrage des configurations. `DisposeAsync` est appelé à la fermeture de l'application. Les anciennes actions Display et Logitech sont reconnues par leurs identifiants existants.

Un plugin proposant plusieurs configurations peut aussi implémenter `IPluginInstanceProvider`. Il indique les actions qui dépendent d'une configuration de démarrage, fournit leur nom, associe les anciennes actions à une configuration et lie les actions à la configuration actuelle. L'application conserve un identifiant stable pour chaque démarrage et propose une entrée par configuration dans la liste des actions. Les plugins qui ne l'implémentent pas conservent leur fonctionnement habituel.

Les plugins s'exécutent avec les droits de l'application : installer uniquement des DLL de confiance.

Les scripts PowerShell s'exécutent sans profil, sans interaction, avec les droits de l'application et une limite de 30 secondes. Leur sortie et leurs erreurs sont consignées dans le journal. N'exécutez que des scripts de confiance.

PowerShell est le choix recommandé pour l'automatisation Windows et l'accès aux outils système. Les événements génériques peuvent aussi être déclenchés manuellement; les autres plugins et pilotes d'EventGhost ne sont pas inclus.

Les actions écran utilisent les API DDC/CI de Windows et nécessitent un moniteur compatible. Choisissez le code d'entrée VCP documenté par le fabricant. Pour Logitech, ajoutez un démarrage dans la section « Démarrage » par récepteur. Chaque configuration choisit un récepteur Unifying (C52B) ou Bolt (C548) compatible et un préfixe d'événements distinct ; l'écoute est lancée automatiquement au chargement ou au rechargement de la configuration. Les actions de changement d'hôte et d'envoi HID apparaissent ensuite une fois par configuration, identifiées par leur préfixe. Si le récepteur d'une configuration change, ses actions suivent la nouvelle configuration. Les anciennes actions « Logitech — démarrer l'écoute » sont converties en configurations de démarrage lors du chargement.
