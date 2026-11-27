# Plugin Planificateur

Identifiant du plugin : `Scheduler`.

Ce plugin émet des événements selon des horaires et fournit des actions pour planifier des tâches temporaires.

## Configuration

Dans **Démarrage**, ajoutez ou modifiez **Planificateur**. Une configuration contient une liste de planifications : choisissez un type, puis utilisez **Ajouter**, **Modifier** ou **Supprimer**. Plusieurs configurations de démarrage indépendantes sont possibles.

- **Quotidienne** : chaque jour à l'heure locale indiquée (`HH:mm`).
- **Hebdomadaire** : aux jours et à l'heure choisis. Les jours sont séparés par des virgules : `0` pour dimanche, `1` pour lundi, jusqu'à `6` pour samedi.
- **Date précise** : à la date locale `AAAA-MM-JJ` et à l'heure choisies.
- **Intervalle** : toutes les N secondes, en continu, tant que le plugin est actif. Le premier déclenchement a lieu après un intervalle complet.
- **Cron** : selon une expression cron à 5 champs `minute heure jour-du-mois mois jour-de-semaine`, évaluée à la minute et en heure locale. Voir les exemples ci-dessous.
- **Inactivité Windows** : après la durée sans saisie clavier ou souris ; la planification se réarme après une nouvelle saisie. Pendant le verrouillage de session ou la veille, le décompte est suspendu et repart au déverrouillage ou à la reprise.

### Expressions cron

Chaque champ accepte `*`, une valeur, une plage `a-b`, un pas `*/n` ou `a-b/n`, et des listes séparées par des virgules. Les mois acceptent `JAN` à `DEC` et les jours `SUN` à `SAT` (insensibles à la casse). Le jour de semaine va de `0` (dimanche) à `6` (samedi), `7` étant aussi accepté pour dimanche.

| Expression | Effet |
| --- | --- |
| `0 * * * *` | À chaque heure pile. |
| `*/15 * * * *` | Toutes les 15 minutes. |
| `30 8 * * 1-5` | Du lundi au vendredi à 08:30. |
| `0 0 1 * *` | Le premier jour de chaque mois à minuit. |

Comme dans un cron classique, si le jour du mois et le jour de semaine sont tous deux restreints, l'un **ou** l'autre suffit (`0 0 1 * MON` déclenche le 1er du mois et tous les lundis). Si l'un des deux vaut `*`, seul l'autre est pris en compte.

## Actions

| Action | Fonction |
| --- | --- |
| **Événement différé** | Émet un événement et son payload après un délai. |
| **Macro différée** | Exécute la macro sélectionnée après un délai, sans bloquer la macro courante. |
| **Compte à rebours** | Émet un événement et son payload à la fin du délai. |
| **Annuler une tâche planifiée** | Annule toutes les tâches actives portant l'identifiant indiqué. |

## Événements

Les planifications et les actions **Événement différé** et **Compte à rebours** émettent le nom d'événement et le payload configurés. Le payload des planifications contient la date et l'heure locale au format ISO 8601. **Macro différée** exécute une macro et n'émet pas elle-même d'événement.

## Prérequis et limites

- Le plugin et l'application doivent être actifs. Une occurrence manquée n'est pas rejouée ; une configuration activée pendant sa minute prévue ne déclenche pas cette occurrence.
- Les délais et intervalles sont exprimés en secondes, de 1 à 2 000 000.
- Les expressions cron ne descendent pas sous la minute : pour une répétition plus rapide, utilisez le type **Intervalle**.
- Plusieurs tâches différées peuvent être actives en même temps, y compris avec le même identifiant : elles s'exécutent alors en parallèle. L'identifiant est comparé sans distinction de casse. **Annuler une tâche planifiée** arrête toutes les tâches actives portant cet identifiant ; l'annulation d'un identifiant absent est signalée dans le journal.
- Une macro supprimée ou désactivée ne sera pas exécutée par **Macro différée** ; l'erreur est inscrite dans le journal. Si la macro a déjà commencé, l'annulation de la tâche demande aussi l'arrêt de son exécution.
- Les actions sont temporaires : leurs tâches sont annulées à la fermeture ou au rechargement de la configuration et ne sont pas reprises au redémarrage. Les planifications de démarrage, y compris les intervalles et les crons, reprennent à l'activation du plugin.
- Pour suspendre la macro courante avant de continuer, utilisez plutôt l'action intégrée **Attendre**.
