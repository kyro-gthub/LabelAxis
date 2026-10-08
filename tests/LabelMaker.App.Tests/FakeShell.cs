using LabelMaker.App.Services;
using LabelMaker.App.ViewModels;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Designs;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Printing;
using LabelMaker.Core.Serialization;
using LabelMaker.Core.Settings;
using LabelMaker.Data.Publisher;

namespace LabelMaker.App.Tests;

/// <summary>
/// Stands in for the windows and message boxes: nothing is shown, every question gets the
/// answer the test set beforehand (or "cancel"), and every message is kept for the test to read.
/// </summary>
public sealed class FakeShell : IShellServices
{
    public List<string> Messages { get; } = new();

    public List<string> Warnings { get; } = new();

    /// <summary>The answer to every "are you sure" question.</summary>
    public bool ConfirmAnswer { get; set; } = true;

    /// <summary>What the next text prompt returns (null = Cancel).</summary>
    public string? PromptAnswer { get; set; }

    /// <summary>What "Remove from all pages" does when it is opened; it returns how many went.</summary>
    public Func<int>? RemoveFromPages { get; set; }

    public int RemoveFromPagesShown { get; private set; }

    /// <summary>The file the next Open box "chooses" (null = Cancel).</summary>
    public string? OpenFileAnswer { get; set; }

    /// <summary>Where the next Save box "saves" (null = Cancel).</summary>
    public string? SaveFileAnswer { get; set; }

    public string? PickOpenFile(string filter, string? title = null) => OpenFileAnswer;

    public string? PickSaveFile(string filter, string defaultFileName, string? title = null) => SaveFileAnswer;

    public string? PickFolder(string? title = null) => null;

    public string? Prompt(string title, string label, string initialValue = "") => PromptAnswer;

    public void Info(string message, string title = "LabelAxis") => Messages.Add(message);

    public void Warn(string message, string title = "LabelAxis") => Warnings.Add(message);

    public bool Confirm(string message, string title = "LabelAxis")
    {
        Messages.Add(message);
        return ConfirmAnswer;
    }

    public bool? ConfirmSaveChanges(string projectTitle) => false;

    public void ShowTextReport(string title, string content) => Messages.Add(content);

    public void ShowNewProject(Action<LabelTemplate> onChosen, Action<LabelDesign, bool>? onDesign = null, bool designsFirst = true)
    {
    }

    public (LabelDesign Design, bool UseDataFields)? ShowDesignGallery() => null;

    public void ShowWelcome(MainViewModel vm)
    {
    }

    public void ShowBulkBarcode()
    {
    }

    public void ShowBarcodeImage()
    {
    }

    public void ShowCalibrationWizard()
    {
    }

    public bool ShowPrintSettings() => false;

    public void ShowSequenceManager()
    {
    }

    public void ShowTemplateManager()
    {
    }

    public void ShowSettings()
    {
    }

    public void ShowAbout()
    {
    }

    public void QuitAndRun(string program, string arguments)
    {
    }

    public void OpenExternal(string target)
    {
    }

    public void ShowDataTable()
    {
    }

    public void ShowPrintPreview()
    {
    }

    public bool ShowPrintDialog() => false;

    public bool ShowThermalPrint() => false;

    public (LabelTemplate Template, bool Save)? ShowThermalLabel() => null;

    public ReprintRequest? ShowPrintHistory() => null;

    public void ShowBarcodeCheck(Func<IReadOnlyList<(string Name, string Value)>> labelBarcodes)
    {
    }

    public (CustomThemeSettings? Theme, bool Removed)? ShowCustomTheme() => null;

    public void ShowAutomation()
    {
    }

    public void ShowFindReplace(bool replace, string? initialQuery)
    {
    }

    public bool ShowLabelSize() => false;

    public bool ShowApplyTextStyle(TextObject source) => false;

    public bool ShowApplyLayout() => false;

    public int ShowRemoveFromPages()
    {
        RemoveFromPagesShown++;
        return RemoveFromPages?.Invoke() ?? 0;
    }

    public (double X, double Y, double Width, double Height)? ShowImageCrop(ImageObject image) => null;

    public T RunJob<T>(string heading, string unit, int total, Func<IProgress<int>, CancellationToken, Task<T>> job) =>
        job(new Progress<int>(), CancellationToken.None).GetAwaiter().GetResult();

    public DataConnectionChoice? ShowDataConnection(DataConnectionModel? existing, string? knownPassword) => null;

    public void ShowPublisherImport(PublisherContent content, string path)
    {
    }

    public string? PromptPassword(string title, string message) => null;

    public ColorRgba? PickColor(ColorRgba initial, string title) => null;

    public (string Name, string? Description, byte[] Picture)? AskSaveDesign(
        ProjectDocument document, LabelDocument page, string suggestedName, Func<string, bool> exists) => null;

    public (BarcodeSpec Spec, string? TextBeside)? ShowGs1Builder(BarcodeSpec? editing) => null;

    public FillChoice? PickFill(ColorRgba initial, ColorRgba? gradientTo, double angleDegrees, string title) => null;
}
