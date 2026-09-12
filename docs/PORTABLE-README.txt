Label Maker 1.0 — portable build
================================

Nothing to install. Unzip this folder anywhere you like — a hard disk, a network
share, a USB stick — and run:

    LabelMaker.exe

There is no runtime to download either: .NET is bundled inside the folder, so
the app runs on a Windows 10 or 11 (64-bit) PC that has never seen .NET.


Where your settings go
----------------------
While "portable.txt" sits next to LabelMaker.exe, everything the app remembers
is kept in the "Data" folder beside it:

    Data\LabelMaker\settings.json      preferences (theme, units, snapping…)
    Data\LabelMaker\templates\         label sheets you import yourself
    Data\LabelMaker\calibration.json   per-printer offsets from the wizard
    Data\LabelMaker\recovery\          auto-save snapshots

Copy the whole folder to another PC and your setup travels with it. Nothing is
written to the registry, and nothing is left behind on the machine.

Delete "portable.txt" if you would rather it behaved like an installed app and
used %APPDATA%\LabelMaker instead. The About box (File ▸ About) always tells you
which of the two is in use.


What is in the box
------------------
LabelMaker.exe      the designer
templates           37 Formtec sheets plus Avery L7160 are built in; add more
                    with File ▸ Label manager ▸ Import JSON/CSV
labelmaker.exe      (in the separate cli package) command-line export,
                    preflight, barcode and template tools


First run
---------
1. File ▸ New project, pick the label sheet you actually own.
2. Design the label; drag from the rulers for guides.
3. Data ▸ Import CSV/Excel if each label differs, then bind fields with
   {ColumnName} tokens in a text box or barcode.
4. Print ▸ Print preview to see the whole sheet before committing paper.
5. If labels land a millimetre or two out, run the calibration wizard once —
   Print ▸ Printer Calibration Wizard — and the offset is remembered per printer.


Uninstalling
------------
Delete the folder.
