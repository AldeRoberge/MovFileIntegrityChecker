# Vérificateur de fichiers MOV / MP4

Cet outil vérifie si vos fichiers vidéo (.mov, .mp4, etc.) sont endommagés ou incomplets.

Il vous indique :

* Si le fichier est valide
* S’il est corrompu
* Quelle portion de la vidéo est encore lisible
* Où la corruption se produit
* Pourquoi le fichier pourrait avoir échoué

---

# Ce que fait l’outil

Pour chaque vidéo, l’outil :

* Vérifie si la structure du fichier est correcte
* Détecte les parties manquantes ou endommagées
* Estime la portion encore lisible de la vidéo
* Génère un rapport détaillé

Si le fichier est corrompu, il crée également un rapport HTML visuel que vous pouvez ouvrir dans votre navigateur.

---

# Comment l’utiliser

Exécutez simplement :

```bash
MovFileIntegrityChecker.exe
```
