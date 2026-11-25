# Système

Ce document décrit les macros, actions et événements fournis par l’application.

## Configuration

Les macros et leurs déclencheurs sont configurés par l’utilisateur ; il n’existe pas de bibliothèque de macros système prédéfinies.

- **Démarrage** est une séquence spéciale d’actions, distincte des macros. Elle s’exécute au démarrage de l’application et après le chargement ou le rechargement de la configuration, après l’initialisation des plugins actifs.
- **Bienvenue** est un exemple ajouté à une nouvelle configuration. Il écoute `Demo.Start` et écrit un message dans le journal ; ce n’est pas une macro système.

## Actions

Les actions système sont proposées lors de l’ajout d’une action à une macro ou à la séquence **Démarrage**.

| Action | Fonction |
| --- | --- |
| **Journaliser** | Écrit un message dans le journal. Le niveau peut être `Debug`, `Information`, `Warning` ou `Error`. |
| **Attendre** | Suspend la séquence pendant une durée en millisecondes, de 0 à 600 000. |
| **Signal sonore** | Émet le signal sonore Windows. |
| **Afficher un message** | Affiche une boîte de dialogue avec un message. |
| **Afficher une notification** | Affiche une notification dans le coin inférieur droit de l’écran, sans boîte de dialogue. Le titre est facultatif (nom de la macro par défaut). Une clé de remplacement facultative met à jour la notification existante de même clé. |
| **Question Oui/Non** | Pose une question ; le résultat transmis à l’action suivante est `Oui` ou `Non`. |
| **Arrêter la macro** | Termine la séquence en cours. |
| **Ouvrir une URL** | Ouvre une URL `http` ou `https` dans le navigateur par défaut. |
| **Script PowerShell** | Exécute un script PowerShell. Le contexte `$eg` permet notamment de journaliser, de définir un résultat et de déclencher un événement. |
| **Déclencher un événement** | Émet un nom d’événement et un payload facultatif. Le payload accepte les variables. Les macros associées à cet événement sont déclenchées. |
| **Activer un élément** / **Désactiver un élément** | Active ou désactive un groupe, une macro, un événement, une action ou un démarrage de plugin. |
| **Jump** | Appelle une macro puis arrête la séquence courante, appelle une macro puis reprend cette séquence, ou saute à une action de la macro courante. |
| **If** | Compare le résultat de l’action précédente avec une valeur, sans tenir compte de la casse (`Est égale à`, `Contient`, `Est différente de`). Si la condition est fausse, l’action suivante est sautée. |

Les actions **Journaliser**, **Afficher un message**, **Afficher une notification**, **Question Oui/Non**, **Ouvrir une URL** et **Déclencher un événement** (payload uniquement) développent les variables suivantes dans leur texte (et dans le titre de la notification ou de la question) :

| Jeton | Valeur |
| --- | --- |
| `{{$eg.result}}` | Résultat de l’action précédente (vide s’il n’y en a pas). |
| `{{$eg.event.name}}` | Nom de l’événement déclencheur. |
| `{{$eg.event.payload}}` | Payload de l’événement déclencheur. |
| `{{$eg.event.sourceId}}` | Source de l’événement déclencheur. |
| `{{$eg.event}}` | Événement déclencheur complet, au format JSON. |
| `{{$eg.variable.Nom}}` | Variable de session définie par `$eg.SetVariable` (nom sensible à la casse). |

Un jeton inconnu est laissé tel quel ; une valeur absente est remplacée par du texte vide. Les jetons peuvent être entourés d’espaces et `$eg` ne tient pas compte de la casse. Pour **Ouvrir une URL**, le texte est développé avant la validation : l’URL finale doit commencer par `http://` ou `https://`. La clé de remplacement d’une notification est développée de la même manière.

## Événements

Les noms ci-dessous sont émis par l’application. Ils peuvent être ajoutés aux déclencheurs d’une macro ; la comparaison des noms ne tient pas compte de la casse.

| Événement | Moment d’émission |
| --- | --- |
| `System.Startup` | Au démarrage de l’application, après les actions de démarrage et le chargement de la configuration. |
| `System.ConfigurationLoaded` | Après le chargement initial ou le rechargement de la configuration. |
| `System.Show` | Lors de l’utilisation de **Afficher EventGhost**, du double-clic sur l’icône de notification ou d’une notification EventGhost. |
| `System.Hide` | Lors de l’utilisation de **Masquer EventGhost** ou de la fermeture de la fenêtre avec son bouton X. |
| `System.Exit` | Lorsque la commande **Sortie** est utilisée depuis l’icône de la zone de notification ; les macros correspondantes sont déclenchées avant la demande de fermeture. |

## Prérequis et limites

- Une macro ne se déclenche que si elle et ses groupes parents sont actifs et si son événement correspondant est activé. Une macro sans déclencheur reste exécutable manuellement.
- Une action désactivée est ignorée.
- Les événements personnalisés ne sont pas limités à cette liste : les actions système et les plugins peuvent émettre leurs propres noms et payloads.
