# Plugin Planificateur

Identifiant du plugin : `Scheduler`.

## Planifications

Dans **Démarrage**, ajoutez ou modifiez **Planificateur**. Une configuration contient une liste de planifications : choisissez un type, puis utilisez **Ajouter**, **Modifier** ou **Supprimer**. Plusieurs configurations de démarrage indépendantes restent possibles.

- **Quotidienne** : émet un événement chaque jour à l'heure locale indiquée (`HH:mm`).
- **Hebdomadaire** : émet un événement aux jours et à l'heure choisis. Les jours sont séparés par des virgules : `0` pour dimanche, `1` pour lundi, jusqu'à `6` pour samedi.
- **Date précise** : émet un événement à la date locale `AAAA-MM-JJ` et à l'heure choisies.
- **Inactivité Windows** : émet un événement après la durée sans saisie clavier ou souris. Il se réarme après une nouvelle saisie.

Le payload contient la date et l'heure locale au format ISO 8601. Les anciennes configurations quotidiennes restent utilisables. Le plugin et l'application doivent être actifs : une occurrence manquée n'est pas rejouée, et une configuration activée pendant sa minute prévue ne déclenche pas cette occurrence.

## Actions de macro

- **Événement différé** : émet un événement et son payload après un délai.
- **Macro différée** : exécute la macro sélectionnée après un délai, sans bloquer la macro courante. Une macro supprimée ou désactivée ne sera pas exécutée ; l'erreur est inscrite dans le journal.
- **Compte à rebours** : émet un événement et son payload à la fin du délai.
- **Minuterie répétitive** : émet un événement et son payload à chaque intervalle, jusqu'à son annulation.
- **Annuler une tâche planifiée** : annule une tâche en cours à partir de son identifiant.

Les délais sont exprimés en secondes, de 1 à 2 000 000. Chaque tâche active doit avoir un identifiant unique, sans distinction de casse ; une deuxième tâche avec le même identifiant est refusée. L'annulation d'un identifiant absent est signalée dans le journal. Si la macro différée a déjà commencé, son annulation demande également l'arrêt de son exécution.

Ces tâches sont temporaires : elles sont annulées à la fermeture ou au rechargement de la configuration et ne sont pas reprises au redémarrage. Pour suspendre la macro courante avant de continuer, utilisez plutôt l'action intégrée **Attendre**.

## Vérification

Les tests ciblés s'exécutent avec `dotnet run --project Plugins\Scheduler.Tests\Scheduler.Tests.csproj`.
Pour vérifier aussi les déclenchements réels à la prochaine minute, ajoutez `-- --clock-integration` (jusqu'à deux minutes).
