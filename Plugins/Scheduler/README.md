# Plugin Planificateur

Identifiant du plugin : `Scheduler`.

Le planificateur émet un événement une fois par jour à l'heure locale configurée. Il s'agit d'un déclencheur en arrière-plan, pas d'une action de macro.

## Configuration

Dans la section **Démarrage**, ajoutez **Planificateur**, puis indiquez une heure au format 24 heures `HH:mm` et le nom de l'événement à émettre. Ajoutez une configuration par heure ou événement souhaité. Par exemple, `07:30` avec `Schedule.Matin`.

Le payload de l'événement est la date et l'heure locale au format ISO 8601. Si l'application est fermée à l'heure planifiée, l'événement manqué n'est pas rejoué au démarrage. Une configuration ajoutée pendant sa minute planifiée ne déclenche pas immédiatement cette occurrence.

Le plugin doit être actif et l'application doit fonctionner à l'heure prévue. Le planificateur est arrêté au rechargement de la configuration et reprend avec les démarrages actifs.
