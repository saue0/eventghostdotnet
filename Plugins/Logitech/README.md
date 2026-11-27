# Plugin Logitech

Identifiant du plugin : `Logitech`.

Le plugin écoute les rapports HID d'un récepteur Logitech Unifying ou Bolt compatible, émet des événements pour les appareils appairés et propose deux actions de commande.

## Configuration

Dans la section **Démarrage**, ajoutez une configuration Logitech, choisissez le récepteur détecté et saisissez un préfixe d'événement (par défaut `Logitech`). Une configuration est associée à un récepteur ; plusieurs récepteurs peuvent être écoutés avec des préfixes distincts. Un même récepteur ne peut pas être démarré avec deux préfixes différents.

## Actions

| Action | Fonction |
| --- | --- |
| **Logitech — changer d'hôte** | Envoie de 1 à 16 octets hexadécimaux aux appareils appairés détectés. |
| **Logitech — envoyer un rapport brut** | Envoie un rapport HID de 1 à 20 octets hexadécimaux. |

Chaque action est associée à un récepteur configuré. Ces commandes dialoguent directement avec le matériel : n'utilisez que des valeurs et rapports connus.

## Événements

Tous les événements commencent par le préfixe configuré dans la section **Démarrage**. Leur forme est `<préfixe>.<événement>` ; par exemple, avec le préfixe `Logitech`, `Battery` devient `Logitech.Battery`.

| Événement | Payload |
| --- | --- |
| `<préfixe>.<touche>` | Aucun. Pour plusieurs touches simultanées, leurs noms sont réunis par `+` dans le suffixe de l'événement. |
| `<préfixe>.Battery` | Pourcentage, état et `DeviceId` hexadécimal identifiant l'emplacement de l'appareil sur le récepteur. |
| `<préfixe>.WIRELESS_DEVICE_STATUS` | Rapport HID brut en hexadécimal. |
| `<préfixe>.Change_Host` | Rapport HID brut en hexadécimal. |

## Reprise après veille ou verrouillage

L'écoute ferme la connexion HID dès que la session devient inactive (verrouillage, veille, déconnexion de console) et la rouvre au retour : les fonctions HID++ sont relues et les notifications des appareils sans fil réarmées, sans recharger le plugin. Un changement d'interface HID (récepteur réénuméré, rebranché, appareil réappairé) réarme de la même façon la connexion. La lecture overlapped en attente est annulée (`CancelIoEx` et jeton de connexion), ce qui évite de rester bloqué sur un récepteur suspendu, et la boucle réessaie toutes les 2 s — ou immédiatement au changement d'interface — si le récepteur est indisponible. Les lignes « Session inactive : écoute Logitech suspendue… », « Reprise de session : écoute Logitech réarmée. » et « Périphérique HID modifié : écoute Logitech réarmée. » précèdent la reconnexion, signalée par « Récepteur Logitech connecté. ».

## Prérequis et limites

- Un récepteur compatible Unifying (C52B) ou Bolt (C548) doit être connecté et détectable.
- Les appareils et commandes disponibles dépendent du récepteur et des appareils appairés.
- Les configurations et actions Logitech doivent être actives et l'écoute du récepteur doit être démarrée. L'écoute est arrêtée au rechargement de la configuration et redémarrée avec les configurations actives.
- Le `DeviceId` est un emplacement sur le récepteur, pas un identifiant matériel permanent. Les noms de touches dépendent des codes reconnus par le plugin ; les codes inconnus sont émis sous forme hexadécimale.

## Exemple PowerShell : notifier un changement de batterie

Créez une macro déclenchée par l'événement de batterie du récepteur, par exemple `Logitech.Battery`, puis ajoutez une action PowerShell :

```powershell
if ($null -eq $eg.Event -or
    [string]::IsNullOrWhiteSpace($eg.Event.SourceId) -or
    $eg.Event.Payload -notmatch '^(?<niveau>\d{1,3})%\s+\S+\s+DeviceId=(?<appareil>[0-9A-F]{2})$') {
    throw "Événement de batterie Logitech invalide."
}

$niveau = [int]$Matches['niveau']
$appareil = $Matches['appareil']
if ($niveau -gt 100) { throw "Niveau de batterie invalide : $niveau" }
$cle = 'Logitech.Battery:{0}:{1}' -f $eg.Event.SourceId, $appareil
$precedent = $eg.ExchangeVariable($cle, $niveau)
if ($null -eq $precedent -or [int]$precedent -ne $niveau) {
    $eg.Notify('Batterie Logitech', "$($eg.Event.SourceId) — appareil $appareil : $niveau %", $cle)
}
```

Cette macro notifie au premier événement, puis uniquement lorsque le pourcentage change. La clé remplace la notification précédente pour le même récepteur et emplacement d'appareil ; une clé différente conserve une notification distincte. La clé dépend du nom affiché du démarrage Logitech et de l'emplacement `DeviceId` : si l'un change, la notification précédente n'est plus associée à la nouvelle clé. La macro est à configurer manuellement et ne modifie pas les événements ou macros existants.

Les plugins peuvent lire ou modifier `host.Result` et utiliser `host.Log(...)` ou `host.RaiseEvent(...)` pendant `ExecuteActionAsync`.
