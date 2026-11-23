# Plugin System Tray Menu

Identifiant du plugin : `SystemTrayMenu`.

Ce plugin ajoute des entrées personnalisées au menu de l'icône EventGhost dans la zone de notification.

## Configuration

Dans la section **Démarrage**, ajoutez **System Tray Menu**, puis créez, modifiez, réordonnez ou supprimez les entrées. Chaque entrée contient un libellé et un nom d'événement ; vous pouvez aussi insérer des séparateurs.

## Actions

Ce plugin ne fournit pas d'action de macro ; il est configuré dans la section **Démarrage**.

## Événements

Le clic sur une entrée déclenche le nom d'événement configuré, sans payload. Un séparateur ne déclenche aucun événement.

Les commandes **Afficher EventGhost**, **Masquer EventGhost** et **Sortie** sont gérées par l'application et ne font pas partie de ce plugin.

## Prérequis et limites

- Une seule configuration de ce plugin est autorisée.
- Le menu personnalisé est installé au démarrage de la configuration active et retiré lors de son arrêt, du rechargement ou du redémarrage des plugins.
- L'icône et les commandes intégrées de l'application restent disponibles séparément.
