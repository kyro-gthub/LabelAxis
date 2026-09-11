namespace LabelMaker.App.Services;

/// <summary>Chrome the view model needs from the shell: file pickers, message boxes, sub-windows.</summary>
public interface IShellServices
{
    string? PickOpenFile(string filter, string? title = null);

    string? PickSaveFile(string filter, string defaultFileName, string? title = null);

    string? PickFolder(string? title = null);

    /// <summary>Single-line text prompt. Returns null on cancel.</summary>
    string? Prompt(string title, string label, string initialValue = "");

    void Info(string message, string title = "Label Maker");

    void Warn(string message, string title = "Label Maker");

    bool Confirm(string message, string title = "Label Maker");

    /// <summary>Yes = save, No = discard, Cancel = null.</summary>
    bool? ConfirmSaveChanges(string projectTitle);

    void ShowTextReport(string title, string content);

    void ShowNewProject(Action<Core.Formtec.LabelTemplate> onChosen);

    void ShowBulkBarcode();

    void ShowBarcodeImage();

    void ShowCalibrationWizard();

    void ShowSequenceManager();

    void ShowTemplateManager();
}
