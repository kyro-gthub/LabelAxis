# LabelAxis click-through tests

These tests start LabelAxis and use it the way a person would — pressing buttons, opening
menus and windows, typing in boxes — through Windows UI Automation, and check what appears.

**Run them only on a separate test PC.** While they run they open windows and take the
keyboard focus for several minutes. On a PC someone is working on, their typing and clicks
would land in the wrong place. The program refuses to start without `--test-pc`.

## What they check

| Test | What happens |
|---|---|
| Starts | LabelAxis opens its main window with the tools, pages and objects. |
| Add text, Undo | A text box appears in the object list; Undo removes it. |
| Add objects | A barcode, a QR code and a table each appear in the object list. |
| Add page, Undo | A page appears in the page list; Undo removes it. |
| Page numbers, page format | Pages are numbered from 1; Apply page format with "add what a page is missing" copies a table onto page 1; Undo takes it away. |
| Start from a design | The gallery lists the designs; "Use this design" opens one with its 6 objects. |
| New project, custom size | The New Project window has "Custom size…", which opens the custom size window. |
| Print window | Opens with the printer list and the preview, closes without printing. |
| Label manager | Lists the label sheets; searching "3100" narrows the list. |
| Find and replace | Finds the text on the sample label. |
| Help guide | Opens; searching "calibration" finds pages. |
| About | Shows the version. |
| Choose Arabic | Settings › Language › Arabic is saved, and LabelAxis offers to restart. |
| Arabic, Central Kurdish, French | Menus, window titles and message-box buttons are in that language. |
| Update offered | A newer version at the update address brings up the update bar; Later puts it away. |

Every test starts its own LabelAxis with a fresh, separate settings folder
(`LabelAxis.exe --data-dir <folder>`), so the test PC's own LabelAxis settings are never used
or changed, and nothing is printed or installed.

## Setting up the test PC

1. A Windows 10 or 11 PC (or virtual machine) that nobody uses while the tests run.
2. Install LabelAxis on it with the small installer (`LabelAxis-Setup-<version>.exe`). This
   also installs the Microsoft .NET 10 Desktop Runtime, which the tests need too.
3. Unzip `LabelAxis-<version>-ui-tests.zip` anywhere, for example to `C:\LabelAxisTests`.
4. Keep the PC awake and unlocked while the tests run (UI Automation cannot reach a locked
   screen). Use a screen resolution of at least 1280 × 800.

## Running them

Open a Command Prompt in the folder and run:

```
LabelAxis.UiTests.exe --test-pc
```

It finds the installed LabelAxis by itself. Other options:

- `--app "D:\Builds\LabelAxis.exe"` — test a particular copy instead of the installed one
  (for example the `artifacts\app-shared` folder of a build).
- `--only Arabic` — run only the tests whose name contains the word.
- `--out D:\Results` — where to put the report (default: `ui-test-results` next to you).

The whole run takes a few minutes. Each test prints PASS or FAIL as it finishes.

## The results

In the results folder:

- `report.txt` — every test, PASS or FAIL, the reason for each failure, and the version and PC.
- `results.xml` — the same in JUnit format, for a build server.
- `<test>.png` — a picture of the screen at the moment a test failed.

The program ends with exit code 0 when everything passed, 1 when a test failed, and 2 when it
did not run (no `--test-pc`, or LabelAxis not found).

## Building the package

On the development PC (this builds; it does not run the tests):

```
powershell -ExecutionPolicy Bypass -File build\ui-tests.ps1
```

`build\release.ps1` makes the same zip as part of a release.

## Adding a test

Tests are methods marked `[Scenario("...")]` in `tests/LabelMaker.UiTests/Scenarios.cs`.
Find elements by their automation id (`AutomationProperties.AutomationId` in the XAML, or an
element's `x:Name`), not by their text, so a test works in every interface language.
