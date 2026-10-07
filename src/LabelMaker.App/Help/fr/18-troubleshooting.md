---
id: troubleshooting
title: Dépannage
category: Reference
order: 3
keywords: problem, help, wrong, error, not working, shifted, blurry, missing font, cut off, blank, smartscreen, warning, barcode not scanning, problème, aide, erreur, ne fonctionne pas, décalé, flou, police manquante, coupé, vide, avertissement, code-barres illisible
---
# Dépannage

## Les étiquettes s'impriment au mauvais endroit

- Vérifiez que la **feuille d'étiquettes** choisie correspond à la boîte des feuilles.
- Gardez l'**échelle** à 100 % et désactivez « ajuster à la page » dans les réglages propres de l'imprimante.
- Lancez l'[[calibration|assistant de calibrage]] pour cette imprimante.

## Le texte est coupé ou trop petit

Regardez `En cas de débordement` dans les paramètres du texte : **Agrandir le cadre** garde tout le texte visible. Avec **Réduire le texte**, la taille baisse jusqu'à ce qu'il tienne. Réduisez le texte, ou agrandissez le cadre, jusqu'à ce que tout s'affiche.

## Une police a un autre aspect sur un autre PC

L'autre PC n'a pas cette police. Les polices fournies avec LabelAxis (les cinq polices arabes et celles de l'application) sont toujours là, et une police ajoutée avec `Ajouter une police…` voyage dans le projet et est ajoutée quand le projet y est ouvert. Pour une police installée seulement dans Windows, installez-la aussi sur ce PC, ou ajoutez son fichier avec `Ajouter une police…` pour que le projet l'emporte. Le contrôle avant impression signale les polices manquantes.

## Les images s'impriment floues

L'image a trop peu de pixels pour sa taille imprimée ; les paramètres de l'image indiquent son DPI. Utilisez une image plus grande, ou imprimez-la plus petite.

## Un code-barres ne se lit pas

Agrandissez-le, gardez des barres sombres sur un fond clair, laissez une marge libre autour, et imprimez plus foncé sur les imprimantes thermiques. Vérifiez que les données conviennent au type de code-barres ([[barcodes|Codes-barres]]).

## L'imprimante n'est pas dans la liste

Installez-la dans Windows, ou pour une imprimante réseau ajoutez-la sous `Imprimantes Wi-Fi…` ([[wifi-printers|Imprimantes Wi-Fi]]). Imprimantes thermiques et Bluetooth : voir [[thermal|Imprimantes d'étiquettes thermiques]].

## « Windows a protégé votre ordinateur » à l'installation

Windows affiche ce message pour les programmes d'un éditeur qu'il ne connaît pas encore. Choisissez **Informations complémentaires**, puis **Exécuter quand même**.

## Aucune mise à jour n'est proposée

- Vérifiez qu'une **adresse de mise à jour** est définie dans `Fichier › Paramètres… › Mises à jour`, ou que cette version a été construite avec une adresse, et que le PC peut l'atteindre.
- Essayez `Aide › Rechercher les mises à jour…` : le message indique ce qui n'a pas marché.
- Une copie portable ne se met pas à jour toute seule ; voir [[files|Projets, enregistrement et paramètres]].

## Autre chose s'est mal passé

LabelAxis protège votre travail dans une copie de récupération et écrit les détails dans un fichier journal (dans `%LOCALAPPDATA%\LabelAxis\logs`). Redémarrez le programme et acceptez la proposition de restaurer votre travail.
