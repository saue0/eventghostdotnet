# Plugin Wallpaper

Identifiant du plugin : `Wallpaper`.

Ce plugin fournit trois actions pour appliquer des fonds d'écran : télécharger des images de Bing, utiliser des images locales ou faire tourner les images déjà préparées dans le dossier temporaire.

## Configuration

Configurez l'action choisie dans une macro ou une séquence de démarrage.

## Actions

| Action | Fonction |
| --- | --- |
| **Fond d'écran — Bing du jour** | Télécharge des images de Bing. Configurez l'index de départ (de 0 à 7) et le nombre d'images (de 1 à 8), sans dépasser huit images au total ; l'index 0 correspond à l'image du jour. Chaque image est limitée à 50 Mo. L'ajout des informations système est facultatif. |
| **Fond d'écran — images locales** | Applique une ou plusieurs images dans l'ordre souhaité. L'ajout des informations système est facultatif. |
| **Fond d'écran — rotation aléatoire** | Utilise les images déjà préparées dans `%TEMP%\EventGhostDotNet\Wallpaper` et les répartit sur les écrans. L'image de départ est choisie au hasard puis les écrans reçoivent les images suivantes en rotation : avec 3 écrans et 5 images, chaque exécution affiche 3 images différentes et le départ change à chaque passage. Aucune configuration n'est nécessaire. |

## Événements

Ce plugin n'émet pas d'événement.

## Prérequis et limites

- Les chemins des images locales doivent rester accessibles à l'utilisateur qui exécute l'application. Les formats proposés sont JPEG, PNG, BMP, GIF et TIFF.
- Lorsque l'ajout des informations système est activé, le plugin inscrit notamment le nom de l'ordinateur, le système, l'heure de démarrage, l'utilisateur et les informations des disques fixes.
- Les images préparées sont placées temporairement dans `%TEMP%\EventGhostDotNet\Wallpaper`. Le plugin applique les images aux écrans via les paramètres de fond d'écran Windows et nettoie les anciennes images générées qu'il ne réutilise plus.
- Les actions sont exécutées une à la fois pour éviter que deux traitements d'images se chevauchent.
- L'accès à Bing nécessite une connexion Internet.
