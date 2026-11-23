# Plugin Wallpaper

Identifiant du plugin : `Wallpaper`.

Ce plugin fournit deux actions pour appliquer des fonds d'écran : télécharger des images de Bing ou utiliser des images locales.

## Configuration

Configurez l'action choisie dans une macro ou une séquence de démarrage.

## Actions

### Fond d'écran — Bing du jour

Configurez l'index de départ (de 0 à 7), le nombre d'images (de 1 à 8) et l'ajout facultatif des informations système. La plage demandée ne peut pas dépasser huit images. L'index 0 correspond à l'image du jour.

Les images sont téléchargées depuis Bing. Chaque image est limitée à 50 Mo. Si l'ajout des informations est activé, le plugin inscrit notamment le nom de l'ordinateur, le système, l'heure de démarrage, l'utilisateur et les informations des disques fixes.

### Fond d'écran — images locales

Sélectionnez une ou plusieurs images dans l'ordre souhaité et choisissez si les informations système doivent être ajoutées.

## Événements

Ce plugin n'émet pas d'événement.

## Prérequis et limites

- Les chemins des images locales doivent rester accessibles à l'utilisateur qui exécute l'application. Les formats proposés sont JPEG, PNG, BMP, GIF et TIFF.
- Les images préparées sont placées temporairement dans `%TEMP%\EventGhostDotNet\Wallpaper`. Le plugin applique les images aux écrans via les paramètres de fond d'écran Windows et nettoie les anciennes images générées qu'il ne réutilise plus.
- Les actions sont exécutées une à la fois pour éviter que deux traitements d'images se chevauchent.
- L'accès à Bing nécessite une connexion Internet.
