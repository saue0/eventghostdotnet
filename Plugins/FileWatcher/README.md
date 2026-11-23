# Plugin Surveillance de dossiers

Identifiant du plugin : `FileWatcher`.

Ce plugin surveille un dossier existant et émet des événements quand des éléments correspondant au filtre changent.

## Configuration

Dans la section **Démarrage**, ajoutez **Surveillance de dossiers**, puis indiquez le chemin absolu d'un dossier existant, un filtre de nom (par exemple `*.txt` ou `*`) et un préfixe d'événement sans espaces (par exemple `Fichiers.Rapports`). Vous pouvez ajouter plusieurs configurations.

## Actions

Ce plugin ne fournit pas d'action de macro ; il est configuré dans la section **Démarrage**.

## Événements

| Événement | Payload |
| --- | --- |
| `<préfixe>.Created` | Chemin complet de l'élément créé. |
| `<préfixe>.Changed` | Chemin complet de l'élément modifié. |
| `<préfixe>.Deleted` | Chemin complet de l'élément supprimé. |
| `<préfixe>.Renamed` | Ancien chemin puis nouveau chemin, séparés par un saut de ligne. |

## Prérequis et limites

- La surveillance porte uniquement sur le dossier indiqué, pas sur ses sous-dossiers.
- Windows peut signaler plusieurs événements `Changed` pour une même écriture ; le plugin transmet les notifications sans les regrouper.
- La surveillance s'arrête au rechargement de la configuration ou au redémarrage des plugins.
