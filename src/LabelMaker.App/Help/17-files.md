---
id: files
title: Projects, saving and settings
category: Reference
order: 2
keywords: save, open, file, labelproj, autosave, recovery, backup, settings, theme, dark, portable, usb, recent, language, arabic, kurdish, sorani, update, new version, upgrade
---
# Projects, saving and settings

## Projects

A project (.labelproj file) holds the label sheet, every page, the pictures it uses and its data connection. Several projects can be open at once, each in its own tab above the label. `File > Open Recent` lists the latest ones.

## Never lose work

- LabelAxis saves a **recovery snapshot** every couple of minutes. If the PC or the program stops unexpectedly, it offers to restore the work the next time it starts.
- Saving keeps the previous version as a backup next to the file.

## Settings

`File > Settings…`: the language, light or dark theme, grid size and snapping distance, auto-save, the welcome window, reopening the last project, updates, and confirmations before removing pages or data.

## Language

`File > Settings… > Language` shows LabelAxis in English, Arabic or Central Kurdish (Sorani). Arabic and Kurdish lay the windows out right to left; technical names such as PDF, QR Code and Wi-Fi stay in English. The new language is used after LabelAxis restarts, which it offers to do. The first time it starts, LabelAxis uses the language of Windows when it is one of these.

## Updates

LabelAxis looks for a newer version about once a day, and whenever you choose `Help > Check for updates…`. When there is one, a bar above the label offers `Update now`, `What's new`, `Later` and `Skip this version`. **Update now** downloads the new version, checks that it is exactly the published file, closes LabelAxis (asking to save your work), installs it and starts LabelAxis again. Your settings, templates and calibration are kept.

Under `File > Settings… > Updates` you can switch the daily check off, or enter the **update address**: the web address (https://, for example a GitHub release folder) or shared folder where LabelAxis updates are published. A version built with an address uses it when the box is left empty.

## Portable copy

The portable version runs from any folder or USB stick. While the file **portable.txt** is next to LabelAxis.exe, settings, your own templates and calibration are kept in the **Data** folder beside it instead of in Windows.

A portable copy does not install updates by itself: when a new version is out, unzip it over the folder (the Data folder is kept).
