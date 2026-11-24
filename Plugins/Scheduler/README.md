# Plugin Planificateur

Identifiant du plugin : `Scheduler`.

Ce plugin émet des événements selon des horaires et fournit des actions pour planifier des tâches temporaires.

## Configuration

Dans **Démarrage**, ajoutez ou modifiez **Planificateur**. Une configuration contient une liste de planifications : choisissez un type, puis utilisez **Ajouter**, **Modifier** ou **Supprimer**. Plusieurs configurations de démarrage indépendantes sont possibles.

- **Quotidienne** : chaque jour à l'heure locale indiquée (`HH:mm`).
- **Hebdomadaire** : aux jours et à l'heure choisis. Les jours sont séparés par des virgules : `0` pour dimanche, `1` pour lundi, jusqu'à `6` pour samedi.
- **Date précise** : à la date locale `AAAA-MM-JJ` et à l'heure choisies.
- **Inactivité Windows** : après la durée sans saisie clavier ou souris ; la planification se réarme après une nouvelle saisie. Pendant le verrouillage de session ou la veille, le décompte est suspendu et repart au déverrouillage ou à la reprise.

## Actions

| Action | Fonction |
| --- | --- |
| **Événement différé** | Émet un événement et son payload après un délai. |
| **Macro différée** | Exécute la macro sélectionnée après un délai, sans bloquer la macro courante. |
| **Compte à rebours** | Émet un événement et son payload à la fin du délai. |
| **Minuterie répétitive** | Émet un événement et son payload à chaque intervalle, jusqu'à son annulation. |
| **Annuler une tâche planifiée** | Annule toutes les tâches actives portant l'identifiant indiqué. |

## Événements

Les planifications et les actions **Événement différé**, **Compte à rebours** et **Minuterie répétitive** émettent le nom d'événement et le payload configurés. Le payload des planifications contient la date et l'heure locale au format ISO 8601. **Macro différée** exécute une macro et n'émet pas elle-même d'événement.

## Prérequis et limites

- Le plugin et l'application doivent être actifs. Une occurrence manquée n'est pas rejouée ; une configuration activée pendant sa minute prévue ne déclenche pas cette occurrence.
- Les délais des tâches sont exprimés en secondes, de 1 à 2 000 000.
- Plusieurs minuteries peuvent être actives en même temps, y compris avec le même identifiant : elles s'exécutent alors en parallèle. L'identifiant est comparé sans distinction de casse. **Annuler une tâche planifiée** arrête toutes les tâches actives portant cet identifiant ; l'annulation d'un identifiant absent est signalée dans le journal.
- Une macro supprimée ou désactivée ne sera pas exécutée par **Macro différée** ; l'erreur est inscrite dans le journal. Si la macro a déjà commencé, l'annulation de la tâche demande aussi l'arrêt de son exécution.
- Les tâches sont temporaires : elles sont annulées à la fermeture ou au rechargement de la configuration et ne sont pas reprises au redémarrage.
- Pour suspendre la macro courante avant de continuer, utilisez plutôt l'action intégrée **Attendre**.

## Vérification

Les tests ciblés s'exécutent avec `dotnet run --project Plugins\Scheduler.Tests\Scheduler.Tests.csproj`. Pour vérifier aussi les déclenchements réels à la prochaine minute, ajoutez `-- --clock-integration` (jusqu'à deux minutes).
