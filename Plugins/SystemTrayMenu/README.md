# Plugin System Tray Menu

Identifiant du plugin : `SystemTrayMenu`.

Ce plugin ajoute des entrées personnalisées au menu de l'icône EventGhost dans la zone de notification. Il ne fournit pas d'actions de macro.

## Configuration

Dans la section **Démarrage**, ajoutez **System Tray Menu**, puis créez, modifiez, réordonnez ou supprimez les entrées. Chaque entrée contient un libellé et un nom d'événement. Vous pouvez également insérer des séparateurs. Une seule configuration de ce plugin est autorisée.

Lorsque l'utilisateur clique sur une entrée, son événement est déclenché. Un séparateur ne déclenche aucun événement.

Les commandes **Afficher EventGhost**, **Masquer EventGhost** et **Sortie** sont gérées par l'application et ne font pas partie de cette configuration. Le plugin ne peut donc pas ajouter ces commandes comme entrées personnalisées.

## Cycle de vie

Le menu personnalisé est installé au démarrage de la configuration active et retiré lors de son arrêt, du rechargement ou du redémarrage des plugins. L'icône et les commandes intégrées de l'application restent gérées séparément.
