---
id: files
title: Projets, enregistrement et paramètres
category: Reference
order: 2
keywords: save, open, file, labelproj, autosave, recovery, backup, settings, theme, dark, portable, usb, recent, language, arabic, kurdish, sorani, persian, farsi, french, spanish, dutch, russian, chinese, hindi, update, new version, upgrade, themes, frost, aurora, midnight, graphite, ember, ocean, sakura, sage, glass, custom theme, own colours, make your own theme, enregistrer, ouvrir, fichier, enregistrement automatique, récupération, sauvegarde, paramètres, thème, sombre, langue, français, mise à jour, nouvelle version, propre thème
---
# Projets, enregistrement et paramètres

## Projets

Un projet (fichier .labelproj) contient la feuille d'étiquettes, toutes les pages, les images qu'il utilise et sa connexion de données. Plusieurs projets peuvent être ouverts à la fois, chacun dans son propre onglet au-dessus de l'étiquette. `Fichier › Ouvrir un fichier récent` liste les derniers.

## Ne jamais perdre votre travail

- LabelAxis enregistre une **copie de récupération** toutes les deux minutes environ. Si le PC ou le programme s'arrête brusquement, il propose de restaurer le travail au démarrage suivant.
- L'enregistrement garde la version précédente comme sauvegarde à côté du fichier (name.labelproj.bak). Vous pouvez le désactiver dans `Fichier › Paramètres…`, sous Enregistrement automatique et récupération.

## Paramètres

`Fichier › Paramètres…` : la langue, l'apparence (huit thèmes, clairs et sombres ; le bouton de thème en haut à droite de la fenêtre principale les change aussi), la taille de la grille et la distance d'accrochage, l'enregistrement automatique, la fenêtre d'accueil, la réouverture du dernier projet, les mises à jour, et les confirmations avant de supprimer des pages ou des données.

## Langue

`Fichier › Paramètres… › Langue` affiche LabelAxis en anglais, arabe, kurde central (sorani), persan, français, espagnol, néerlandais, russe, chinois simplifié ou hindi. L'arabe, le kurde et le persan disposent les fenêtres de droite à gauche ; les noms techniques comme PDF, QR Code et Wi-Fi restent en anglais. La nouvelle langue est utilisée après le redémarrage de LabelAxis, qu'il propose de faire. Au premier démarrage, LabelAxis utilise la langue de Windows si c'est l'une de celles-ci.

## Mises à jour

LabelAxis cherche une version plus récente environ une fois par jour, et chaque fois que vous choisissez `Aide › Rechercher les mises à jour…`. Quand il y en a une, une barre au-dessus de l'étiquette propose `Mettre à jour maintenant`, `Nouveautés`, `Plus tard` et `Ignorer cette version`. **Mettre à jour maintenant** télécharge la nouvelle version, vérifie que c'est exactement le fichier publié, ferme LabelAxis (en proposant d'enregistrer votre travail), l'installe et redémarre LabelAxis. Vos paramètres, formats et calibrages sont conservés.

Dans `Fichier › Paramètres… › Mises à jour`, vous pouvez désactiver la vérification quotidienne, ou saisir l'**adresse de mise à jour** : l'adresse web (https://, par exemple un dossier de publication GitHub) ou le dossier partagé où les mises à jour de LabelAxis sont publiées. Une version construite avec une adresse l'utilise quand la case est vide.

## Copie portable

La version portable fonctionne depuis n'importe quel dossier ou clé USB. Tant que le fichier **portable.txt** se trouve à côté de LabelAxis.exe, les paramètres, vos formats et les calibrages sont gardés dans le dossier **Data** à côté, plutôt que dans Windows.

Une copie portable n'installe pas les mises à jour toute seule : quand une nouvelle version sort, décompressez-la par-dessus le dossier (le dossier Data est conservé).

## Votre propre thème

`Fichier › Paramètres… › Créer votre propre thème…` (ou le bouton de thème en haut à droite) crée un thème à vos couleurs : choisissez un thème de départ, une couleur principale, deux couleurs de halo, un arrière-plan si vous voulez, et l'intensité des halos. LabelAxis change au fur et à mesure, vous voyez donc tout de suite le résultat ; les couleurs du texte sont choisies pour vous afin de rester lisibles. Votre thème est listé avec les autres ; `Supprimer mon thème` le supprime.
