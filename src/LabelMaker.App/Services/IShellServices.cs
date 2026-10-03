namespace LabelMaker.App.Services;

/// <summary>A database connection chosen in the connect dialog, with the password typed for it.</summary>
public sealed record DataConnectionChoice(Core.Model.DataConnectionModel Connection, string? Password);

/// <summary>Chrome the view model needs from the shell: file pickers, message boxes, sub-windows.</summary>
public interface IShellServices
{
    string? PickOpenFile(string filter, string? title = null);

    string? PickSaveFile(string filter, string defaultFileName, string? title = null);

    string? PickFolder(string? title = null);

    /// <summary>Single-line text prompt. Returns null on cancel.</summary>
    string? Prompt(string title, string label, string initialValue = "");

    void Info(string message, string title = "LabelAxis");

    void Warn(string message, string title = "LabelAxis");

    bool Confirm(string message, string title = "LabelAxis");

    /// <summary>Yes = save, No = discard, Cancel = null.</summary>
    bool? ConfirmSaveChanges(string projectTitle);

    void ShowTextReport(string title, string content);

    void ShowNewProject(Action<Core.Formtec.LabelTemplate> onChosen);

    /// <summary>The start window (new, open, recent, sheets); runs the chosen action on <paramref name="vm"/>.</summary>
    void ShowWelcome(ViewModels.MainViewModel vm);

    void ShowBulkBarcode();

    void ShowBarcodeImage();

    void ShowCalibrationWizard();

    /// <summary>Advanced print settings (content mode, start position, skip mask, record range,
    /// scale, outlines/crop marks). Returns true if the user confirmed (Print…).</summary>
    bool ShowPrintSettings();

    void ShowSequenceManager();

    void ShowTemplateManager();

    void ShowSettings();

    /// <summary>The imported records in a resizable grid (moved out of the old bottom dock).</summary>
    void ShowDataTable();

    /// <summary>Full-sheet print preview: every sheet exactly as it will print.</summary>
    void ShowPrintPreview();

    /// <summary>The app's own print dialog (printer, copies, scale). Returns true if a job was sent.</summary>
    bool ShowPrintDialog();

    /// <summary>Thermal printing (ZPL/TSPL, dot preview). Returns true if a job was sent.</summary>
    bool ShowThermalPrint();

    /// <summary>Define a thermal roll. Null on cancel.</summary>
    (Core.Formtec.LabelTemplate Template, bool Save)? ShowThermalLabel();

    /// <summary>
    /// The print log. Returns the job the user chose to print again, or null; the
    /// caller runs the reprint (it may need to open another project first).
    /// </summary>
    Core.Printing.ReprintRequest? ShowPrintHistory();

    /// <summary>
    /// Opens (or brings back) the modeless find-and-replace window, focused on the
    /// replace field when <paramref name="replace"/> is set.
    /// </summary>
    void ShowFindReplace(bool replace, string? initialQuery);

    /// <summary>Change the working label size without starting a new project. Returns true if it changed.</summary>
    bool ShowLabelSize();

    /// <summary>Copies <paramref name="source"/>'s formatting to text boxes on other pages. Returns true if applied.</summary>
    bool ShowApplyTextStyle(LabelMaker.Core.Model.Objects.TextObject source);

    /// <summary>
    /// Interactive crop for <paramref name="image"/>. Returns the chosen normalised
    /// rectangle, or null on cancel; the caller applies it through the undo stack.
    /// </summary>
    (double X, double Y, double Width, double Height)? ShowImageCrop(Core.Model.Objects.ImageObject image);

    /// <summary>
    /// Runs a long job with a progress window (count so far of <paramref name="total"/>
    /// <paramref name="unit"/>) and Cancel. Throws <see cref="OperationCanceledException"/>
    /// if the user cancelled, or whatever the job threw.
    /// </summary>
    T RunJob<T>(string heading, string unit, int total, Func<IProgress<int>, CancellationToken, Task<T>> job);

    /// <summary>
    /// Connect to a database: choose the kind, sign in, pick a table or type a query and
    /// preview it. Returns what to read, or null on cancel. <paramref name="existing"/>
    /// opens the form filled in, to change the current connection.
    /// </summary>
    DataConnectionChoice? ShowDataConnection(Core.Model.DataConnectionModel? existing, string? knownPassword);

    /// <summary>What was read from a Publisher file, to tick and bring into the label or the records.</summary>
    void ShowPublisherImport(LabelMaker.Data.Publisher.PublisherContent content, string path);

    /// <summary>Asks for a password (masked). Null on cancel.</summary>
    string? PromptPassword(string title, string message);

    /// <summary>Colour chooser with palette, RGBA sliders and an eyedropper. Null on cancel.</summary>
    Core.Model.ColorRgba? PickColor(Core.Model.ColorRgba initial, string title);
}
