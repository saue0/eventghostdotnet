# EventGhost .NET

Application de bureau Windows en WPF et .NET 9, inspirée du fonctionnement d'EventGhost. Cette implémentation est indépendante et ne reprend pas son code source.

## Démarrer

Depuis ce dossier, avec le SDK .NET 9 ou plus récent :

```powershell
dotnet run
```

Dans VS Code, la tâche « Release : arrêter, publier et redémarrer » demande la sortie des instances Release, publie un exécutable autonome et les plugins dans `bin\live`, puis démarre cette version. Les fichiers `.deps.json` des plugins et l'icône nécessaires au fonctionnement sont également inclus. Une confirmation peut être nécessaire si la configuration a été modifiée ; si la fermeture est annulée ou prend plus de 30 secondes, la publication est annulée. Lors de la première utilisation avec une ancienne version ne prenant pas en charge cette demande, quittez-la manuellement depuis la zone de notification puis relancez la tâche.

## Fonctions disponibles

- Consulter la configuration en arborescence : chaque macro contient ses événements déclencheurs et sa séquence d'actions. Le journal est affiché à côté et la séparation est redimensionnable.
- Gérer les démarrages de plugins et une séquence d'actions exécutées après leur initialisation, dans une section dédiée en tête de l'arborescence. Les actions de démarrage sont lancées au démarrage de l'application et au rechargement de la configuration.
- Activer ou désactiver chaque démarrage de plugin avec sa case ou son menu contextuel. Le changement annule les macros en cours et redémarre les plugins actifs, sans relancer les actions de démarrage ; les actions associées à une instance inactive échouent avec un message dans le journal. Pour un plugin sans instances associées aux actions, celles-ci sont indisponibles lorsque tous ses démarrages sont inactifs. L'état est conservé après enregistrement et les anciens démarrages sont actifs par défaut.
- Sélectionner une macro ou l'un de ses éléments pour agir sur cette macro ; sélectionner une action pour la modifier ou la supprimer.
- Modifier un événement individuellement par double-clic ou avec son crayon, ou le supprimer avec sa corbeille. Une macro peut ne contenir aucun événement et être lancée manuellement.
- Activer ou désactiver chaque événement avec sa case à cocher ; un événement désactivé reste dans la configuration, mais ne déclenche pas la macro.
- Créer, modifier et supprimer des macros avec zéro ou plusieurs événements déclencheurs ; une nouvelle macro n'a aucun événement par défaut et la réception d'un événement associé exécute la macro.
- Organiser les macros en groupes, les déplacer entre groupes et conserver l'état ouvert ou fermé du démarrage, des groupes et des macros.
- Utiliser le clic droit sur un élément de configuration pour accéder à ses actions disponibles, ou sur une zone vide de l'arborescence pour ajouter un groupe.
- Réordonner les groupes, les macros et les actions, y compris les actions de démarrage, avec les commandes Monter et Descendre de leur menu contextuel.
- Glisser-déposer les groupes, macros, événements, actions et démarrages de plugins pour les réordonner ; les macros peuvent changer de groupe, les événements de macro et les actions de macro ou de section Démarrage. Les événements ne peuvent pas être placés dans Démarrage. Les déplacements qui rendraient une référence invalide sont refusés ; la réorganisation des démarrages de plugins prend effet au prochain démarrage ou rechargement. Enregistrez la configuration pour conserver les déplacements.
- Glisser une ligne d'événement reçu du journal sur une macro pour ajouter cet événement à ses déclencheurs. Seule la ligne glissée est utilisée ; les lignes sans événement et les doublons sont refusés.
- Modifier une macro ou une action par double-clic ou avec son crayon ; la corbeille demande confirmation avant suppression.
- Associer, modifier et supprimer une séquence d'actions : écrire dans le journal, attendre, émettre un signal sonore, afficher un message, ouvrir une URL HTTP(S), déclencher un événement avec payload facultatif ou exécuter un script PowerShell.
- Choisir une action dans un sélecteur regroupé par source : Système, Logitech ou Display.
- Fournir un contexte à chaque action et plugin : `Log` écrit dans le journal, `Result` transmet une valeur à l'action suivante et `RaiseEvent` déclenche un événement.
- Ajouter des branchements système : `Jump` peut appeler une autre macro en arrêtant ou reprenant la macro courante, ou sauter vers une action de celle-ci ; `If` compare le résultat de l'action précédente à une chaîne (égalité, présence ou différence) et saute l'action suivante si la condition est fausse.
- Activer ou désactiver une macro ou une action avec sa case à cocher ou une action système ciblée. Une macro inactive ne réagit plus aux événements, mais reste exécutable manuellement ; ses actions inactives sont ignorées.
- Changer l'entrée d'un écran compatible DDC/CI via son code VCP 0x60.
- Télécharger jusqu'à huit images de Bing ou sélectionner une liste d'images locales, choisir si les informations système sont ajoutées, puis appliquer les images comme fonds d'écran. Les informations comprennent l'espace libre et le système de fichiers de chaque disque fixe ; les lecteurs amovibles et réseau sont exclus. Le nom de Windows 11 est identifié à partir de la version système lorsque le registre indique encore Windows 10. Les images préparées, nommées `Image_1`, `Image_2`, etc., sont réutilisées dans `%TEMP%\EventGhostDotNet\Wallpaper` pour éviter l'accumulation de fichiers.
- Écouter les rapports HID d'un ou plusieurs récepteurs Logitech Unifying ou Bolt, déclencher des événements de touches et de batterie, changer d'hôte et envoyer des rapports bruts.
- Réduire la fenêtre principale dans la zone de notification avec le bouton de fermeture, puis la restaurer depuis l'icône gérée par l'application.
- Choisir dans Fichier > Préférences de démarrage si l'application doit s'afficher ou démarrer minimisée dans la zone de notification.
- Afficher en permanence les commandes Afficher EventGhost, Masquer EventGhost et Sortie dans le menu de l'icône d'application. Elles déclenchent `System.Show`, `System.Hide` et `System.Exit`; elles sont disponibles même sans utiliser le plugin System Tray Menu.
- Configurer avec le plugin System Tray Menu des entrées personnalisées et leurs événements, ainsi que des séparateurs. Les commandes de l'application ne font pas partie de cette configuration.
- Exécuter une macro ou déclencher manuellement un événement exact pour exécuter les macros correspondantes.
- Exécuter l'élément sélectionné : une macro complète ou une action seule.
- Sélectionner plusieurs lignes du journal avec Ctrl ou Maj pour les copier avec Ctrl+C ou « Copier la sélection », et effacer le journal avec « Effacer le journal ». Le défilement automatique est suspendu tant qu'une ligne est sélectionnée. Sélectionner un seul événement reçu pour copier son nom ou l'ajouter aux déclencheurs de la macro sélectionnée ; enregistrer la configuration pour conserver la modification.
- Déclencher `System.Startup` au démarrage et `System.ConfigurationLoaded` après le chargement ou le rechargement de la configuration.
- Recharger manuellement la configuration depuis le menu Fichier; les macros en cours sont annulées et les écoutes Logitech de l'ancienne configuration sont libérées avant le remplacement.
- Enregistrer et recharger les démarrages de plugins et les macros en JSON dans `%LOCALAPPDATA%\EventGhostDotNet\configuration.json`. Les anciens fichiers contenant directement un tableau de macros restent lisibles.
- À la sortie réelle de l'application, la fenêtre est ramenée au premier plan pour choisir d'enregistrer ou d'abandonner les modifications non enregistrées, ou d'annuler la fermeture. Windows peut toutefois refuser son activation automatique.

Le nouveau document JSON contient `PluginStartups`, `StartupActions` et `Macros`; chaque macro contient une liste `EventNames`, par exemple `"EventNames": ["Unify.Host 1", "Unify.Host 2"]`. Les anciens fichiers où la racine est directement le tableau des macros sont lus et convertis. Les anciens événements système préfixés par `EGDN` sont convertis vers `System.*` au chargement. Les anciens champs `EventName` et `AdditionalEventNames` ne sont plus pris en charge.

Les actions système peuvent activer ou désactiver une macro ou une action d'une macro, même après renommage. Une macro déjà en cours continue son exécution lorsqu'elle est désactivée. Désactiver une action ne l'interrompt pas si elle est déjà en cours et n'arrête pas les ressources qu'elle a déjà démarrées. Les changements d'état prennent effet immédiatement, sans sauvegarde automatique ; ils sont conservés si la configuration est ensuite enregistrée. Un changement d'état effectué par une action système ne déclenche pas à lui seul l'avertissement de fermeture, contrairement à un changement manuel. Par défaut, les macros et actions sont actives.

Les comparaisons de `If` ne tiennent pas compte de la casse et utilisent le résultat de l'action précédente ; une action désactivée, absente ou en erreur n'a pas de résultat. Chaque action commence avec sa valeur configurée comme résultat par défaut. Un script PowerShell peut utiliser `$eg.Log("message")`, `$eg.Result = "OK"` (ou `$eg.SetResult("OK")`) et `$eg.RaiseEvent("Nom", "contenu")` ; sa sortie standard est aussi envoyée au journal. Les appels récursifs de macros et les boucles de saut sont bornés pour éviter une exécution sans fin.

### Variables et événement dans PowerShell

Les scripts partagent des variables de session avec `$eg.SetVariable(nom, valeur)`, `$eg.GetVariable(nom)`, `$eg.HasVariable(nom)` et `$eg.RemoveVariable(nom)`. `$eg.ExchangeVariable(nom, valeur)` remplace atomiquement une valeur et retourne la précédente, ou `$null` si elle n'existait pas. `HasVariable` distingue une variable absente d'une variable contenant `$null`. Les noms distinguent les majuscules et les valeurs doivent être sérialisables en JSON. Elles restent disponibles après un rechargement de configuration ou un redémarrage des plugins, mais disparaissent à la fermeture de l'application.

`$eg.Event` contient `Name`, `Payload` et `SourceId` pour l'événement déclencheur ; il vaut `$null` lors d'une exécution manuelle. Pour un événement émis par un démarrage de plugin, `SourceId` contient le nom affiché dans la section « Démarrage », par exemple `Logitech — Bureau`. Les autres événements peuvent avoir un `SourceId` nul. Les événements de même nom d'une même source sont traités dans leur ordre d'arrivée.

Le payload d'une batterie Logitech contient le pourcentage, l'état et le numéro de l'appareil, par exemple `85% discharging DeviceId=01`. Ce numéro hexadécimal est un emplacement sur le récepteur, pas un identifiant matériel permanent. Le texte ` DeviceId=...` est ajouté à l'ancien payload : adaptez les scripts qui comparaient le payload entier. `$eg.Notify(titre, message)` envoie une notification silencieuse au centre de notifications Windows ; son affichage dépend des paramètres de notification de Windows. Un clic sur la notification affiche EventGhost. Les notifications ne sont pas prises en charge lorsque l'application est exécutée en administrateur.

Exemple : créer une macro déclenchée par l'événement de batterie du récepteur, par exemple `Logitech.Battery`, et y ajouter une action PowerShell :

```powershell
if ($null -eq $eg.Event -or
    [string]::IsNullOrWhiteSpace($eg.Event.SourceId) -or
    $eg.Event.Payload -notmatch '^(?<niveau>\d{1,3})%\s+\S+\s+DeviceId=(?<appareil>[0-9A-F]{2})$') {
    throw "Événement de batterie Logitech invalide."
}

$niveau = [int]$Matches['niveau']
$appareil = $Matches['appareil']
if ($niveau -gt 100) { throw "Niveau de batterie invalide : $niveau" }
$cle = 'Logitech.Battery:{0}:{1}' -f $eg.Event.SourceId, $appareil
$precedent = $eg.ExchangeVariable($cle, $niveau)
if ($null -eq $precedent -or [int]$precedent -ne $niveau) {
    $eg.Notify('Batterie Logitech', "$($eg.Event.SourceId) — appareil $appareil : $niveau %")
}
```

Cette macro notifie au premier événement et ensuite uniquement lorsque le pourcentage change. Elle est à configurer manuellement et ne modifie pas les événements ou macros existants.

Les plugins peuvent lire ou modifier `host.Result` et utiliser `host.Log(...)` ou `host.RaiseEvent(...)` pendant `ExecuteActionAsync`.

## Structure du code

Les éléments de l'arborescence sont regroupés par type dans `Items` : modèle, interface XAML et méthodes associées de `MainWindow`. Ils dérivent de `ConfigurationItem` ; les conteneurs dérivent de `ConfigurationContainer` et exposent leurs enfants. Le parent est synchronisé avec les collections et n'est pas sérialisé. Les interactions communes, notamment le menu contextuel et le glisser-déposer, restent partagées.

## Modules de plugins

L'application découvre les DLL dans le dossier `Plugins` à côté de son exécutable au démarrage. Display, Logitech, System Tray Menu et Wallpaper sont compilés et copiés dans ce dossier automatiquement. Le menu System Tray Menu ne fournit que des entrées personnalisées; l'application conserve toujours ses propres commandes et son icône. Pour ajouter un plugin externe, créer une bibliothèque .NET 9 Windows qui référence le projet `PluginContracts` et expose une classe publique sans paramètre implémentant `IPlugin`; placer sa DLL, son fichier `.deps.json` et ses dépendances dans un sous-dossier distinct de `Plugins`, puis redémarrer l'application.

Le plugin fournit un identifiant stable, ses actions et leur formulaire, et exécute ses actions via `IPlugin`. Il peut également fournir des configurations de démarrage et les exécuter avec `StartupAsync`; les plugins sans initialisation spécifique héritent d'un démarrage sans effet. `ResetAsync` doit arrêter et libérer les ressources créées par le plugin : il est appelé lors du rechargement de configuration et avant le redémarrage des configurations. `DisposeAsync` est appelé à la fermeture de l'application. Les plugins intégrés comprennent Display, Logitech, System Tray Menu et Wallpaper.

Un plugin proposant plusieurs configurations peut aussi implémenter `IPluginInstanceProvider`. Il indique les actions qui dépendent d'une configuration de démarrage, fournit leur nom, associe les anciennes actions à une configuration et lie les actions à la configuration actuelle. L'application conserve un identifiant stable pour chaque démarrage et propose une entrée par configuration dans la liste des actions. Les plugins qui ne l'implémentent pas conservent leur fonctionnement habituel.

Les plugins s'exécutent avec les droits de l'application : installer uniquement des DLL de confiance.

Les scripts PowerShell s'exécutent sans profil, sans interaction, avec les droits de l'application et une limite de 30 secondes. PowerShell est configuré en UTF-8 pour sa sortie et ses erreurs, consignées dans le journal. N'exécutez que des scripts de confiance.

PowerShell est le choix recommandé pour l'automatisation Windows et l'accès aux outils système. Les événements génériques peuvent aussi être déclenchés manuellement; les autres plugins et pilotes d'EventGhost ne sont pas inclus.

Les actions écran utilisent les API DDC/CI de Windows et nécessitent un moniteur compatible. Choisissez le code d'entrée VCP documenté par le fabricant. Pour Logitech, ajoutez un démarrage dans la section « Démarrage » par récepteur. Chaque configuration choisit un récepteur Unifying (C52B) ou Bolt (C548) compatible et un préfixe d'événements distinct ; l'écoute est lancée automatiquement au chargement ou au rechargement de la configuration. Les actions de changement d'hôte et d'envoi HID apparaissent ensuite une fois par configuration, identifiées par leur préfixe. Si le récepteur d'une configuration change, ses actions suivent la nouvelle configuration. Les anciennes actions « Logitech — démarrer l'écoute » sont converties en configurations de démarrage lors du chargement.
