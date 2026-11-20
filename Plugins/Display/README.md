# Plugin Écran

Identifiant du plugin : `Display`.

Ce plugin fournit l'action **Écran — changer l'entrée** pour sélectionner une source d'entrée sur un moniteur compatible DDC/CI.

## Utilisation

1. Ajoutez l'action dans une macro.
2. Choisissez le moniteur physique dans la liste.
3. Saisissez la valeur de la source d'entrée en décimal ou en hexadécimal, par exemple `15` ou `0x0F`.

L'action utilise le code VCP d'entrée `0x60`. La valeur à saisir est la source d'entrée demandée, et non le code VCP. Consultez la documentation du fabricant pour connaître les valeurs prises en charge par le moniteur.

## Prérequis et limites

- Un écran compatible DDC/CI doit être connecté et accessible à Windows.
- Les écrans sans moniteur physique DDC/CI ne sont pas proposés.
- Le moniteur peut refuser la commande ou ne pas prendre en charge la valeur demandée.
- L'action ne modifie aucun autre réglage de l'écran.
