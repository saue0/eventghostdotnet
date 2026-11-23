# Plugin Écran

Identifiant du plugin : `Display`.

Ce plugin sélectionne une source d'entrée sur un moniteur compatible DDC/CI.

## Configuration

Ajoutez l'action à une macro, choisissez le moniteur physique dans la liste, puis saisissez la source d'entrée en décimal ou en hexadécimal, par exemple `15` ou `0x0F`. La valeur à saisir est la source demandée, et non le code VCP `0x60`. Consultez la documentation du fabricant pour connaître les valeurs prises en charge.

## Actions

| Action | Fonction |
| --- | --- |
| **Écran — changer l'entrée** | Envoie la commande VCP d'entrée `0x60` au moniteur choisi. |

## Événements

Ce plugin n'émet pas d'événement.

## Prérequis et limites

- Un écran compatible DDC/CI doit être connecté et accessible à Windows.
- Les écrans sans moniteur physique DDC/CI ne sont pas proposés.
- Le moniteur peut refuser la commande ou ne pas prendre en charge la valeur demandée.
- L'action ne modifie aucun autre réglage de l'écran.
