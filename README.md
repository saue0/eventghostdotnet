# EventGhost .NET

Application de bureau Windows en WPF et .NET 9, inspirée du fonctionnement d'EventGhost. Cette implémentation est indépendante et ne reprend pas son code source.

## Démarrer

Depuis ce dossier, avec le SDK .NET 9 ou plus récent :

```powershell
dotnet run
```

Depuis une version publiée, lancez `EventGhostDotNet.exe`.

Dans VS Code, la tâche « Release : arrêter, publier et redémarrer » demande la sortie des instances Release, publie l'application et les plugins dans `bin\live`, puis démarre cette version. La publication Windows x64 nécessite le .NET 9 Desktop Runtime x64 installé sur chaque poste. Conservez tout le dossier publié, y compris les DLL, les fichiers `.deps.json` des plugins, l'icône et les README. Une confirmation peut être nécessaire si la configuration a été modifiée ; si la fermeture est annulée ou prend plus de 30 secondes, la publication est annulée. Lors de la première utilisation avec une ancienne version ne prenant pas en charge cette demande, quittez-la manuellement depuis la zone de notification puis relancez la tâche.

## Tests

```powershell
dotnet test Tests\EventGhostDotNet.Core.Tests\EventGhostDotNet.Core.Tests.csproj
```

Les tests couvrent la logique sans interface de `Core` : journal ANSI, migration et version de la configuration, écriture atomique, manifeste de plugins, règles d'exécution des macros, action `If` et comparaison des versions de publication. Ils s'exécutent sans Windows ni WPF. La solution `EventGhostDotNet.sln` regroupe l'application, le contrat, les plugins, `Core` et les tests ; la CI les compile et les exécute sur Windows.

## Publication et installation

Le script `build\publish.ps1` publie l'application et ses plugins pour Windows x64 et ARM64, puis vérifie l'exécutable, `runtimeconfig.json`, les 7 plugins et `plugins.manifest`.

```powershell
# Dépendant du runtime : .NET 9 Desktop Runtime x64 ou ARM64 requis sur le poste.
.\build\publish.ps1

# Autonome : le runtime est inclus, aucun prérequis .NET sur le poste.
.\build\publish.ps1 -SelfContained

# ARM64 uniquement, autonome, avec installateur.
.\build\publish.ps1 -Runtime win-arm64 -SelfContained -Installer

# Signature Authenticode de l'exécutable et de l'installateur (certificat dans CurrentUser\My).
.\build\publish.ps1 -SelfContained -Installer -CertificateThumbprint <empreinte>
```

Les sorties vont dans `bin\publish\<runtime>-<variante>` et `bin\installer`. L'installateur Inno Setup installe dans `Program Files\EventGhostDotNet`, ajoute un raccourci au menu Démarrer (et facultativement au bureau) et se désinstalle proprement ; sa compilation demande Inno Setup 6.3 ou plus récent. L'installateur x64 fonctionne aussi sur Windows 11 ARM64 par émulation, tandis que l'installateur ARM64 ne s'installe que sur Windows ARM64.

Les DLL des plugins ne sont pas signées après publication : leur empreinte SHA-256 figure dans `plugins.manifest` et toute signature modifierait le fichier, ce que le chargeur refuserait. Seuls l'exécutable principal et l'installateur portent la signature Authenticode. Sans certificat, `-CertificateThumbprint` est omis et les fichiers sont produits non signés.

Fichier > Vérifier les mises à jour interroge la dernière release GitHub (`https://api.github.com/repos/saue0/eventghostdotnet/releases/latest`) et compare son étiquette à la version de l'application. Il n'installe rien automatiquement : il propose d'ouvrir la page de téléchargement. Publiez les versions avec une étiquette `v1.0.0` (ou `1.0.0`), la version de l'application étant définie par `<Version>` dans `EventGhostDotNet.csproj`.

L'application n'accepte qu'une instance par session : un second lancement réactive la fenêtre existante au lieu d'ouvrir une autre instance. Les erreurs inattendues sont consignées dans `%LOCALAPPDATA%\EventGhostDotNet\errors.log` (fichier précédent conservé en `.old`) ; une erreur d'interface affiche un message et laisse l'application ouverte.

## Fonctions disponibles

- Consulter la configuration en arborescence : chaque macro contient ses événements déclencheurs et sa séquence d'actions. Le journal est affiché à côté et la séparation est redimensionnable.
- Gérer les démarrages de plugins et une séquence d'actions exécutées après leur initialisation, dans une section dédiée en tête de l'arborescence. Les actions de démarrage sont lancées au démarrage de l'application et au rechargement de la configuration.
- Activer ou désactiver chaque démarrage de plugin depuis son menu contextuel. Le changement annule les macros en cours et redémarre les plugins actifs, sans relancer les actions de démarrage ; les actions associées à une instance inactive échouent avec un message dans le journal. Pour un plugin sans instances associées aux actions, celles-ci sont indisponibles lorsque tous ses démarrages sont inactifs. L'état est conservé après enregistrement et les anciens démarrages sont actifs par défaut.
- Sélectionner une macro ou l'un de ses éléments pour agir sur cette macro ; sélectionner une action pour la modifier ou la supprimer.
- Modifier un événement individuellement par double-clic ou depuis son menu contextuel, qui permet aussi de le supprimer. Une macro peut ne contenir aucun événement et être lancée manuellement.
- Activer ou désactiver chaque événement depuis son menu contextuel ; un événement désactivé reste dans la configuration, mais ne déclenche pas la macro.
- Créer, modifier et supprimer des macros avec zéro ou plusieurs événements déclencheurs ; une nouvelle macro n'a aucun événement par défaut et la réception d'un événement associé exécute la macro.
- Organiser les macros sous une racine « Configuration » en groupes et sous-groupes, les déplacer entre groupes et conserver l'état ouvert ou fermé du démarrage, des groupes et des macros. Sélectionner la racine pour créer un groupe principal, ou un groupe pour créer un sous-groupe.
- Désactiver un groupe depuis son menu contextuel ou avec Ctrl+D : aucun événement ne déclenche les macros du groupe ni de ses sous-groupes. Leur exécution manuelle reste possible et les macros déjà lancées continuent ; l'état propre de chaque macro et sous-groupe est conservé.
- Utiliser le clic droit sur un élément de configuration pour accéder à ses actions disponibles, ou sur une zone vide de l'arborescence pour ajouter un groupe.
- Utiliser les menus Configuration, Édition et Affichage pour ajouter ou modifier des éléments, exécuter ou désactiver la sélection, développer l'arborescence, filtrer le journal et gérer les éléments avec couper, copier, coller, annuler ou rétablir. L'arborescence affiche des lignes de liaison, des icônes par type et un X rouge sur les éléments désactivés. Un dossier correspond à un groupe de macros ; les actions utilisent une icône d'exécution, de terminal pour PowerShell ou d'outil pour les plugins.
- Consulter le README de l'application et ceux des plugins chargés dans le menu Aide. La documentation Markdown est rendue avec ses titres, listes, tableaux, liens et blocs de code directement par WPF ; une indication apparaît si le fichier est absent ou illisible. Aucun runtime de navigateur n'est nécessaire.
- Rechercher dans les noms des groupes, macros, événements et actions avec Ctrl+F puis F3 ; exporter une macro ou une action sélectionnée comme script Python. Les actions sans équivalent direct sont signalées dans le script pour conversion manuelle.
- Réordonner les groupes, les macros et les actions, y compris les actions de démarrage, avec les commandes Monter et Descendre de leur menu contextuel.
- Glisser-déposer les groupes, macros, événements, actions et démarrages de plugins pour les réordonner ; déposer un groupe au centre d'un autre pour en faire un sous-groupe, ou sur la racine pour en faire un groupe principal. Les macros peuvent changer de groupe, les événements de macro et les actions de macro ou de section Démarrage. Les événements ne peuvent pas être placés dans Démarrage. Les déplacements qui créeraient un cycle ou rendraient une référence invalide sont refusés ; la réorganisation des démarrages de plugins prend effet au prochain démarrage ou rechargement. Enregistrez la configuration pour conserver les déplacements.
- Glisser une ligne d'événement reçu du journal sur une macro pour ajouter cet événement à ses déclencheurs. Seule la ligne glissée est utilisée ; les lignes sans événement et les doublons sont refusés.
- Modifier une macro ou une action par double-clic ou depuis son menu contextuel ; la suppression demande confirmation.
- Associer, modifier et supprimer une séquence d'actions : écrire dans le journal, attendre, émettre un signal sonore, afficher un message, afficher une notification, poser une question Oui/Non, arrêter la macro courante, ouvrir une URL HTTP(S), déclencher un événement avec payload facultatif ou exécuter un script PowerShell. L'éditeur de script repose sur AvalonEdit : numéros de ligne, indentation automatique et coloration syntaxique par la grammaire TextMate PowerShell (`TextMateSharp`). Les séquences emoji du script sont rendues en couleur, dans l'éditeur comme dans les notifications (`Emoji.Wpf`).
- Choisir des actions système et des actions fournies par les plugins installés.
- Fournir un contexte à chaque action et plugin : `Log` écrit dans le journal, `Result` transmet une valeur à l'action suivante et `RaiseEvent` déclenche un événement.
- Ajouter des branchements système : `Jump` peut appeler une autre macro en arrêtant ou reprenant la macro courante, ou sauter vers une action de celle-ci ; `If` compare le résultat de l'action précédente à une chaîne (égalité, présence ou différence) et saute l'action suivante si la condition est fausse ; `If / Jump / Else Jump` applique la même comparaison, puis saute à une action de la macro courante, saute à une autre macro (elle est exécutée puis la macro courante s'arrête), continue ou arrête la macro sur chacune des branches « Si vrai » et « Sinon ».
- Activer ou désactiver une macro ou une action depuis son menu contextuel ou avec une action système ciblée. Une macro inactive ne réagit plus aux événements, mais reste exécutable manuellement ; ses actions inactives sont ignorées.
- Réduire la fenêtre principale dans la zone de notification avec le bouton de fermeture, puis la restaurer depuis l'icône gérée par l'application.
- Choisir dans Fichier > Préférences de démarrage si l'application doit s'afficher ou démarrer minimisée dans la zone de notification, et lancer EventGhost avec Windows : la case écrit la valeur `EventGhostDotNet` dans `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` avec la ligne de commande citée suivie de `--minimized`, argument qui force aussi un démarrage minimisé.
- Afficher en permanence les commandes Afficher EventGhost, Masquer EventGhost, Historique des notifications et Sortie dans le menu de l'icône d'application. Elles déclenchent `System.Show`, `System.Hide` et `System.Exit`; elles restent disponibles même sans menu personnalisé. L'historique liste les notifications de la session : une notification de même clé remplace son entrée précédente, le bouton ✕ retire une entrée et un clic sur une entrée affiche EventGhost. La fenêtre s'ajuste à son contenu, entre 220 et 460 pixels de haut ; les bulles se réduisent quand le titre ou le message est vide, mesurent leur hauteur sur le contenu (et non avec `SizeToContent`, mal dimensionné sur une fenêtre transparente en DPI élevé) et plafonnent un message long à 140 pixels, terminé par une ellipse.
- Exécuter une macro ou déclencher manuellement un événement exact pour exécuter les macros correspondantes.
- Exécuter l'élément sélectionné : une macro complète ou une action seule.
- Sélectionner plusieurs lignes du journal avec Ctrl ou Maj pour les copier avec Ctrl+C ou « Copier la sélection », et effacer le journal avec Ctrl+L. Le menu Affichage filtre aussi les lignes de macros et d'actions, et permet d'afficher les niveaux Debug, d'indenter et d'horodater les lignes. Les cases de niveau permettent de masquer ou réafficher indépendamment chaque niveau ; toutes sont cochées au démarrage. Les lignes masquées comptent dans la limite de 5 000 entrées et sont aussi supprimées lors de l'effacement. Les couleurs ANSI SGR (16 couleurs, 256 couleurs et RGB, texte et fond) restent affichées et priment sur la couleur du niveau ; les séquences correspondantes ne sont pas copiées. Le défilement automatique est suspendu tant qu'une ligne est sélectionnée. Sélectionner un seul événement reçu pour copier son nom, l'ajouter aux déclencheurs de la macro sélectionnée ou le rejouer avec son payload et sa source d'origine (bouton « Rejouer l'événement ») ; enregistrer la configuration pour conserver la modification.
- Avec « Indenter le journal », chaque événement est une racine du journal : sa ligne part du niveau zéro, même lorsqu'une action l'émet, et la macro qu'il déclenche démarre au niveau 1. Les macros appelées et les messages d'action sont ensuite décalés selon leur contexte parent, jusqu'à 64 niveaux ; les exécutions indépendantes conservent chacune leur propre indentation.
- Déclencher `System.Startup` au démarrage et `System.ConfigurationLoaded` après le chargement ou le rechargement de la configuration.
- Recharger manuellement la configuration depuis le menu Fichier; les macros en cours sont annulées et les ressources des plugins actifs sont libérées avant le remplacement.
- Vérifier l'intégrité des plugins : `Plugins\plugins.manifest` associe chaque DLL intégrée à une empreinte SHA-256 calculée à la compilation. Une empreinte absente déclenche un avertissement de confiance (refus par défaut) et une empreinte différente fait refuser le plugin ; « Fichier > Recharger les plugins » arrête les plugins, décharge leurs contextes de chargement, les recharge et redémarre les configurations.
- Enregistrer et recharger les démarrages de plugins et les macros en JSON dans `%LOCALAPPDATA%\EventGhostDotNet\configuration.json`. Les anciens fichiers contenant directement un tableau de macros restent lisibles. Les options d'affichage (barre d'outils, développement automatique, heure, horodatage, indentation, niveaux et catégories du journal) sont réenregistrées dès qu'elles changent et restaurées au démarrage.
- À la sortie réelle de l'application, la fenêtre est ramenée au premier plan pour choisir d'enregistrer ou d'abandonner les modifications non enregistrées, ou d'annuler la fermeture. Windows peut toutefois refuser son activation automatique.

Le nouveau document JSON contient `PluginStartups`, `StartupActions` et `Macros`; chaque macro contient une liste `EventNames`, par exemple `"EventNames": ["System.Startup", "Custom.Event"]`. Les anciens fichiers où la racine est directement le tableau des macros sont lus et convertis. Les anciens événements système préfixés par `EGDN` sont convertis vers `System.*` au chargement. Les anciens champs `EventName` et `AdditionalEventNames` ne sont plus pris en charge.

Les actions système « Activer un élément » et « Désactiver un élément » ciblent un groupe, une macro, un événement, une action (y compris de démarrage) ou un démarrage de plugin. Les anciennes actions ciblant une macro ou une action restent compatibles. Les événements sont identifiés par leur macro et leur nom ; les références suivent leur modification ou leur déplacement dans l'arbre. Une macro déjà en cours continue son exécution lorsqu'elle est désactivée. Désactiver une action ne l'interrompt pas si elle est déjà en cours et n'arrête pas les ressources qu'elle a déjà démarrées. Modifier l'état d'un démarrage de plugin annule les macros en cours et redémarre les plugins, sans relancer les actions de démarrage. Les changements d'état prennent effet immédiatement, sans sauvegarde automatique ; ils sont conservés si la configuration est ensuite enregistrée. Un changement d'état effectué par une action système ne déclenche pas à lui seul l'avertissement de fermeture, contrairement à un changement manuel. Par défaut, les groupes, macros et actions sont actifs.

Les comparaisons de `If` et de `If / Jump / Else Jump` ne tiennent pas compte de la casse et utilisent le résultat de l'action précédente ; une action désactivée, absente ou en erreur n'a pas de résultat. La réponse à « Question Oui/Non » est `Oui` ou `Non` et peut donc être testée par `If`. « Arrêter la macro » termine uniquement la séquence de la macro courante. Dans `If / Jump / Else Jump`, chaque branche choisit indépendamment : sauter à une action de la macro courante, sauter à une macro (la macro cible est exécutée au niveau d'appel suivant, puis la macro courante s'arrête), continuer à l'action suivante ou arrêter la macro. Un saut vers une action ne peut viser qu'une action de la macro courante et est refusé quand l'action seule est exécutée ; un saut vers une macro reste valide après un copier/coller ou un déplacement de l'action, et la duplication d'une macro remappe les cibles de ses branches (action ou macro). Chaque action commence avec sa valeur configurée comme résultat par défaut. Un script PowerShell peut utiliser `$eg.Log("message")`, `$eg.Result = "OK"` (ou `$eg.SetResult("OK")`) et `$eg.RaiseEvent("Nom", "contenu")` ; sa sortie standard est aussi envoyée au journal. Les appels récursifs de macros et les boucles de saut sont bornés pour éviter une exécution sans fin.

Les actions « Journaliser », « Afficher un message », « Afficher une notification », « Question Oui/Non », « Ouvrir une URL » et « Déclencher un événement » (payload uniquement) remplacent les jetons `{{$eg.result}}` (résultat de l'action précédente), `{{$eg.event.name}}`, `{{$eg.event.payload}}`, `{{$eg.event.sourceId}}`, `{{$eg.event}}` (JSON de l'événement) et `{{$eg.variable.Nom}}` (variable de session définie par `$eg.SetVariable`). Un jeton inconnu est laissé tel quel ; une valeur absente est remplacée par du texte vide. Pour « Ouvrir une URL », le texte est développé avant la validation : l'URL finale doit toujours commencer par `http://` ou `https://`. La clé de remplacement d'une notification accepte les mêmes jetons. Les actions `If` et `If / Jump / Else Jump` développent aussi ces jetons dans « Valeur à tester » (vide = résultat précédent, soit `{{$eg.result}}`) et dans « Valeur attendue » : on peut donc comparer `{{$eg.result}}` à `{{$eg.variable.Nom}}`.

### Variables et événement dans PowerShell

L'action « Journaliser » permet de choisir son niveau. `$eg.Log("message")` utilise le niveau Information. Pour préciser le niveau dans un script, utilisez `$eg.Log("message", "Debug")`, `"Information"`, `"Warning"` ou `"Error"`. La sortie standard et `stderr` des programmes restent au niveau Information : leur texte ne suffit pas à déterminer si une erreur s'est produite. Les erreurs d'exécution détectées par l'application sont journalisées au niveau Erreur.

Les scripts partagent des variables de session avec `$eg.SetVariable(nom, valeur)`, `$eg.GetVariable(nom)`, `$eg.HasVariable(nom)` et `$eg.RemoveVariable(nom)`. `$eg.ExchangeVariable(nom, valeur)` remplace atomiquement une valeur et retourne la précédente, ou `$null` si elle n'existait pas. `HasVariable` distingue une variable absente d'une variable contenant `$null`. Les noms distinguent les majuscules et les valeurs doivent être sérialisables en JSON. Elles restent disponibles après un rechargement de configuration ou un redémarrage des plugins, mais disparaissent à la fermeture de l'application.

`$eg.Event` contient `Name`, `Payload` et `SourceId` pour l'événement déclencheur ; il vaut `$null` lors d'une exécution manuelle. Pour un événement émis par un démarrage de plugin, `SourceId` contient le nom affiché dans la section « Démarrage ». Les autres événements peuvent avoir un `SourceId` nul. Les événements de même nom d'une même source sont traités dans leur ordre d'arrivée.

`$eg.Notify(titre, message)` affiche une notification dans le coin inférieur droit de l'écran principal, au-dessus de la zone de notification. La position tient compte du facteur de mise à l'échelle (DPI) de Windows et est recalculée si la résolution, la barre des tâches ou la mise à l'échelle change. Avec une clé non vide en troisième argument, `$eg.Notify(titre, message, clé)`, une nouvelle notification remplace celle de même clé. Les clés distinguent les majuscules et restent identiques après un redémarrage ; utilisez des clés distinctes pour des notifications indépendantes. Sans clé, les notifications s'empilent (trois au maximum). Une notification disparaît après quelques secondes, ou dès qu'on clique dessus : un clic affiche EventGhost. Le survol de la souris suspend la disparition. Les notifications sont affichées par l'application elle-même et ne dépendent pas des paramètres de notification de Windows. Les 50 dernières notifications de la session restent consultables avec « Historique des notifications » dans le menu de l'icône ; un clic sur une entrée de l'historique affiche EventGhost. Cet historique n'est pas conservé après la fermeture de l'application.

## Structure du code

Les éléments de l'arborescence sont regroupés par type dans `Items` : modèle, interface XAML et méthodes associées de `MainWindow`. Ils dérivent de `ConfigurationItem` ; les conteneurs dérivent de `ConfigurationContainer` et exposent leurs enfants. Le parent est synchronisé avec les collections et n'est pas sérialisé. Les interactions communes, notamment le menu contextuel et le glisser-déposer, restent partagées.

Pour le catalogue des macros, actions et événements fournis par l'application, consultez le [README du système](Items/README.md).

## Modules de plugins

L'application découvre les DLL dans le dossier `Plugins` à côté de son exécutable au démarrage. Les plugins intégrés sont compilés et copiés dans ce dossier automatiquement. Pour créer et installer un plugin externe ou l'intégrer à l'application, consultez le [guide de création d'un plugin](Plugins/CREER_UN_PLUGIN.md).

Le plugin fournit un identifiant stable, ses actions et leur formulaire, et exécute ses actions via `IPlugin`. Il peut également fournir des configurations de démarrage et les exécuter avec `StartupAsync`; les plugins sans initialisation spécifique héritent d'un démarrage sans effet. `ResetAsync` doit arrêter et libérer les ressources créées par le plugin : il est appelé lors du rechargement de configuration et avant le redémarrage des configurations. `DisposeAsync` est appelé à la fermeture de l'application. Pour les fonctions, événements, configurations et prérequis des plugins intégrés, consultez le README de chaque plugin dans [Plugins/](Plugins/).

Un plugin proposant plusieurs configurations peut aussi implémenter `IPluginInstanceProvider`. Il indique les actions qui dépendent d'une configuration de démarrage, fournit leur nom, associe les anciennes actions à une configuration et lie les actions à la configuration actuelle. L'application conserve un identifiant stable pour chaque démarrage et propose une entrée par configuration dans la liste des actions. Les plugins qui ne l'implémentent pas conservent leur fonctionnement habituel.

Les plugins s'exécutent avec les droits de l'application : installer uniquement des DLL de confiance.

Les actions de script utilisent PowerShell 7. L'application cherche d'abord le chemin configuré dans Fichier > Préférences PowerShell, puis `pwsh.exe` dans le `PATH` et les emplacements d'installation habituels. Si PowerShell 7 reste introuvable, le script s'exécute avec Windows PowerShell 5.1 et un avertissement est journalisé une fois par session ; le chemin peut être défini ou réinitialisé dans les préférences, et il est conservé avec la configuration. Pour un script nécessitant l'ancien moteur, cochez « Utiliser Windows PowerShell 5.1 » dans son action. Vérifiez la compatibilité des scripts et modules lors du changement de version.

Les scripts PowerShell s'exécutent sans profil, sans interaction et avec les droits de l'application. La limite est de 30 secondes par défaut ; cochez « Autoriser une exécution sans limite de temps » dans l'action pour la supprimer. Même sans limite de temps, un script en cours est annulé lors du rechargement de la configuration ou de la fermeture de l'application. PowerShell est configuré en UTF-8 pour sa sortie et ses erreurs, consignées dans le journal. N'exécutez que des scripts de confiance.

PowerShell est le choix recommandé pour l'automatisation Windows et l'accès aux outils système. Les événements génériques peuvent aussi être déclenchés manuellement; les autres plugins et pilotes d'EventGhost ne sont pas inclus.
