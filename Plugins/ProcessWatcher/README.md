# Plugin Surveillance de processus

Identifiant du plugin : `ProcessWatcher`.

Ce plugin détecte les transitions de présence d'un processus Windows et émet des événements. Il n'exécute ni n'arrête le processus surveillé.

## Configuration

Dans la section **Démarrage**, ajoutez **Surveillance de processus**, puis saisissez le nom du processus (avec ou sans extension `.exe`) et un préfixe d'événement sans espaces. Par exemple, le processus `notepad` avec le préfixe `Process.Notepad`.

Vous pouvez ajouter plusieurs configurations pour surveiller différents noms de processus.

## Événements et limites

Le plugin émet `<préfixe>.Started` lorsqu'aucun processus de ce nom n'était actif et qu'au moins un apparaît, puis `<préfixe>.Stopped` lorsque le dernier disparaît. Le payload contient le nom du processus. Si un processus est déjà actif au démarrage de la surveillance, aucun événement initial n'est émis.

L'état est vérifié toutes les secondes : un processus très bref peut démarrer et se terminer entre deux vérifications sans être détecté. La surveillance s'arrête au rechargement de la configuration ou au redémarrage des plugins.
