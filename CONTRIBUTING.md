# Contributing to LabelAxis

Thank you for helping. Bug reports, label sheets, translations and code are all welcome.

## Reporting a bug or asking for a feature

Use **Issues › New issue** and pick a form. For a printing problem, the printer model and the
label sheet (for example Formtec 3100) help the most; a small `.labelproj` file that shows the
problem is even better. For anything that could be a security problem, see
[SECURITY.md](SECURITY.md) instead.

## Building and testing

You need Windows 10 or 11 and the .NET 10 SDK (the version in `global.json`, or newer).

```bash
dotnet build LabelMaker.slnx -c Release
dotnet test tests/LabelMaker.Tests -c Release
dotnet test tests/LabelMaker.App.Tests -c Release
```

`build\test.ps1` runs both test suites with coverage and fails if a part of the program falls
below its minimum. Every pull request runs the same checks on GitHub.

The click-through tests in `tests/LabelMaker.UiTests` move the mouse and type, so run them
only on a separate test PC (see [docs/UI-TESTS.md](docs/UI-TESTS.md)).

## Making a change

1. Read [docs/DEVELOPER.md](docs/DEVELOPER.md) for the map of the code and the rules that keep
   it in shape: logic goes in `LabelMaker.Core`, every edit goes through
   `DocumentSession.Execute` so it can be undone, and error messages for the user use
   `Faults.IsReportable`.
2. Keep to the style of the surrounding code (`.editorconfig`). The build treats every warning
   as an error.
3. Add or update tests for what you change.
4. Write interface text in plain English. New text needs translations in
   `src/LabelMaker.App/Languages/*.json`; a pull request may leave those to the maintainer.
5. Open a pull request against `main` and describe what changes for the person using LabelAxis.

When something has to give, the priorities in the [README](README.md#priorities) decide:
stability and data safety first, then printing at the true size, accurate label sheets and
reliable barcodes.

## Licence

By contributing you agree that your work is published under the project's
[MIT licence](LICENSE).
