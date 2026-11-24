# Plugin Écran

Identifiant du plugin : `Display`.

Ce plugin pilote un moniteur compatible DDC/CI : source d'entrée, luminosité et contraste, et lecture de l'entrée courante.

## Configuration

Ajoutez l'action à une macro, choisissez le moniteur physique dans la liste, puis saisissez la valeur demandée. Le dialogue affiche la valeur actuelle lue sur l'écran (`GetVCPFeature`) pour le contrôle concerné.

- **Entrée** : source demandée en décimal ou en hexadécimal, par exemple `15` ou `0x0F`. La valeur à saisir est la source, et non le code VCP `0x60`.
- **Luminosité** et **contraste** : pourcentage de 0 à 100, converti sur la valeur maximale rapportée par le moniteur.
- **Lire l'entrée courante** : aucune valeur à saisir ; l'action renvoie le résultat à l'action suivante (`Entrée actuelle : 15 (0x0F)`) et l'écrit dans le journal.

Consultez la documentation du fabricant pour connaître les valeurs d'entrée prises en charge.

## Actions

| Action | Fonction |
| --- | --- |
| **Écran — changer l'entrée** | Envoie la commande VCP d'entrée `0x60` au moniteur choisi. |
| **Écran — luminosité** | Envoie la commande VCP `0x10` avec la valeur convertie depuis le pourcentage. |
| **Écran — contraste** | Envoie la commande VCP `0x12` avec la valeur convertie depuis le pourcentage. |
| **Écran — lire l'entrée courante** | Lit le code VCP `0x60` et publie la valeur courante comme résultat de l'action. |

## Événements

Ce plugin n'émet pas d'événement.

## Prérequis et limites

- Un écran compatible DDC/CI doit être connecté et accessible à Windows.
- Les écrans sans moniteur physique DDC/CI ne sont pas proposés.
- Le moniteur peut refuser la commande, refuser la lecture ou ne pas prendre en charge la valeur demandée ; l'échec est alors signalé par une exception.
- Après une veille, les derniers réglages appliqués par le plugin (entrée, luminosité, contraste) sont réessayés une fois, deux secondes après la reprise, car les moniteurs refusent souvent DDC/CI juste après le réveil.
- Les actions ne modifient aucun autre réglage de l'écran.
