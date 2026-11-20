# Plugin Surveillance de dossiers

Identifiant du plugin : `FileWatcher`.

Ce plugin surveille un dossier existant et émet des événements quand des éléments correspondant au filtre changent.

## Configuration

Dans la section **Démarrage**, ajoutez **Surveillance de dossiers**, puis indiquez :

- le chemin absolu d'un dossier existant;
- un filtre de nom, par exemple `*.txt` ou `*`;
- un préfixe d'événement sans espaces, par exemple `Fichiers.Rapports`.

Une configuration surveille uniquement le dossier indiqué, pas ses sous-dossiers. Vous pouvez ajouter plusieurs configurations pour surveiller plusieurs dossiers.

## Événements

Le plugin émet les événements `<préfixe>.Created`, `<préfixe>.Changed`, `<préfixe>.Deleted` et `<préfixe>.Renamed`. Le payload contient le chemin complet concerné. Pour un renommage, il contient d'abord l'ancien chemin, puis le nouveau, séparés par un saut de ligne.

Windows peut signaler plusieurs événements `Changed` pour une même opération d'écriture. Le plugin transmet les notifications reçues sans les regrouper. La surveillance s'arrête au rechargement de la configuration ou au redémarrage des plugins.
