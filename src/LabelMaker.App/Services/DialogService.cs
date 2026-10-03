using System.IO;
using System.Windows;
using LabelMaker.App.ViewModels;
using LabelMaker.App.Views;
using LabelMaker.Core.Formtec;
using Microsoft.Win32;

namespace LabelMaker.App.Services;

/// <summary>WPF implementation of <see cref="IShellServices"/> — file dialogs, message boxes, sub-windows.</summary>
public sealed class DialogService : IShellServices
{
    private readonly Window _owner;
    private readonly AppEnvironment _env;
    private MainViewModel? _vm;

    public DialogService(Window owner, AppEnvironment env)
    {
        _owner = owner;
        _env = env;
    }

    public void Bind(MainViewModel vm) => _vm = vm;

    /// <summary>The window in front, so a message raised from a dialog appears over that dialog rather than behind it.</summary>
    private Window Front => Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible) ?? _owner;

    public string? PickOpenFile(string filter, string? title = null)
    {
        var dlg = new OpenFileDialog { Filter = filter, Title = title ?? "Open" };
        if (_env.Settings.LastProjectDirectory is { } dir && Directory.Exists(dir))
        {
            dlg.InitialDirectory = dir;
        }

        if (dlg.ShowDialog(_owner) == true)
        {
            _env.Settings.LastProjectDirectory = Path.GetDirectoryName(dlg.FileName);
            return dlg.FileName;
        }

        return null;
    }

    public string? PickSaveFile(string filter, string defaultFileName, string? title = null)
    {
        var dlg = new SaveFileDialog { Filter = filter, FileName = defaultFileName, Title = title ?? "Save" };
        if (_env.Settings.LastProjectDirectory is { } dir && Directory.Exists(dir))
        {
            dlg.InitialDirectory = dir;
        }

        if (dlg.ShowDialog(_owner) == true)
        {
            _env.Settings.LastProjectDirectory = Path.GetDirectoryName(dlg.FileName);
            return dlg.FileName;
        }

        return null;
    }

    public T RunJob<T>(string heading, string unit, int total, Func<IProgress<int>, CancellationToken, Task<T>> job) =>
        JobProgressWindow.Run(Front, heading, unit, total, job);

    public string? PickFolder(string? title = null)
    {
        var dlg = new OpenFolderDialog { Title = title ?? "Choose folder" };
        return dlg.ShowDialog(_owner) == true ? dlg.FolderName : null;
    }

    public string? Prompt(string title, string label, string initialValue = "")
    {
        var win = new Views.InputWindow(title, label, initialValue) { Owner = _owner };
        return win.ShowDialog() == true ? win.Value : null;
    }

    public void Info(string message, string title = "LabelAxis") =>
        MessageBox.Show(Front, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void Warn(string message, string title = "LabelAxis") =>
        MessageBox.Show(Front, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public bool Confirm(string message, string title = "LabelAxis") =>
        MessageBox.Show(Front, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    public void ShowPublisherImport(LabelMaker.Data.Publisher.PublisherContent content, string path)
    {
        if (_vm is not null)
        {
            new PublisherImportWindow(_vm, content, path) { Owner = Front }.ShowDialog();
        }
    }

    public string? PromptPassword(string title, string message)
    {
        var win = new PasswordWindow(title, message) { Owner = Front };
        return win.ShowDialog() == true ? win.Password : null;
    }

    public DataConnectionChoice? ShowDataConnection(LabelMaker.Core.Model.DataConnectionModel? existing, string? knownPassword)
    {
        var win = new DataConnectionWindow(existing, knownPassword) { Owner = Front };
        return win.ShowDialog() == true ? win.Result : null;
    }

    public bool? ConfirmSaveChanges(string projectTitle)
    {
        MessageBoxResult result = MessageBox.Show(
            _owner,
            $"Save changes to \"{projectTitle}\" before continuing?",
            "LabelAxis",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return result switch
        {
            MessageBoxResult.Yes => true,
            MessageBoxResult.No => false,
            _ => null,
        };
    }

    public void ShowTextReport(string title, string content) =>
        new TextReportWindow(title, content) { Owner = _owner }.ShowDialog();

    public void ShowNewProject(Action<LabelTemplate> onChosen)
    {
        var win = new NewProjectWindow(_env.Templates, _env.Settings.FavoriteTemplateIds) { Owner = _owner };
        if (win.ShowDialog() == true && win.SelectedTemplate is { } template)
        {
            onChosen(template);
        }
    }

    public void ShowWelcome(MainViewModel vm)
    {
        var win = new WelcomeWindow(_env) { Owner = _owner };
        if (win.ShowDialog() != true)
        {
            return;
        }

        switch (win.Action)
        {
            case WelcomeAction.NewFromTemplateDialog:
                vm.NewCommand.Execute(null);
                break;
            case WelcomeAction.NewFromTemplate when win.ChosenTemplate is { } template:
                vm.NewProjectFromTemplate(template);
                break;
            case WelcomeAction.NewThermal:
                vm.NewThermalLabelCommand.Execute(null);
                break;
            case WelcomeAction.OpenDialog:
                vm.OpenCommand.Execute(null);
                break;
            case WelcomeAction.OpenRecent when win.ProjectPath is { } path:
                vm.OpenProjectPath(path);
                break;
            case WelcomeAction.ImportPublisher:
                vm.OpenPublisherPagesCommand.Execute(null);
                break;
            case WelcomeAction.LabelManager:
                vm.TemplateManagerCommand.Execute(null);
                break;
        }
    }

    public void ShowBulkBarcode() =>
        new BulkBarcodeWindow(_env) { Owner = _owner }.ShowDialog();

    public void ShowBarcodeImage() =>
        new BarcodeImageWindow() { Owner = _owner }.ShowDialog();

    public void ShowCalibrationWizard()
    {
        if (_vm is null)
        {
            return;
        }

        new CalibrationWizardWindow(_env, _vm.Session.Project) { Owner = _owner }.ShowDialog();
    }

    public bool ShowPrintSettings()
    {
        if (_vm is null)
        {
            return false;
        }

        var win = new PrintSettingsWindow(_vm.Session, this) { Owner = Front };
        return win.ShowDialog() == true;
    }

    public void ShowSequenceManager()
    {
        if (_vm is null)
        {
            return;
        }

        new SequenceManagerWindow(_vm.Session) { Owner = _owner }.ShowDialog();
    }

    public void ShowTemplateManager()
    {
        var win = new TemplateManagerWindow(_env) { Owner = _owner };
        if (win.ShowDialog() == true && win.SelectedTemplate is { } template)
        {
            _vm?.ApplyTemplate(template);
        }
    }

    public void ShowSettings()
    {
        if (_vm is null)
        {
            return;
        }

        new SettingsWindow(_env, _vm) { Owner = _owner }.ShowDialog();
    }

    public void ShowDataTable()
    {
        if (_vm is not null)
        {
            new DataTableWindow(_vm) { Owner = _owner }.ShowDialog();
        }
    }

    public void ShowPrintPreview()
    {
        if (_vm is null)
        {
            return;
        }

        try
        {
            new PrintPreviewWindow(_vm.Session, _vm.PrintServer, this) { Owner = Front }.ShowDialog();
        }
        catch (Exception ex)
        {
            Warn("Could not build the preview:\n\n" + ex.Message);
        }
    }

    private FindReplaceWindow? _findReplace;

    public void ShowFindReplace(bool replace, string? initialQuery)
    {
        if (_vm is null)
        {
            return;
        }

        // One window for the session: reopening keeps the query, options and results.
        if (_findReplace is null || !_findReplace.IsLoaded)
        {
            _findReplace = new FindReplaceWindow(_vm.Session) { Owner = _owner };
            EventHandler follow = (_, _) => _findReplace?.Retarget(_vm.Session);
            _vm.ActiveTabChanged += follow;
            _findReplace.Closed += (_, _) =>
            {
                _vm.ActiveTabChanged -= follow;
                _findReplace = null;
            };
            _findReplace.Show();
        }

        _findReplace.Present(replace, initialQuery);
    }

    public bool ShowPrintDialog()
    {
        if (_vm is null)
        {
            return false;
        }

        var win = new PrintWindow(_vm.Session, _vm.PrintServer, this, _env.PrintHistory) { Owner = Front };
        bool sent = win.ShowDialog() == true;

        // Reprint chosen from the history inside the dialog: run it now that the dialog is closed.
        return win.Reprint is { } reprint ? _vm.Reprint(reprint) : sent;
    }

    public bool ShowThermalPrint()
    {
        if (_vm is null)
        {
            return false;
        }

        var win = new ThermalPrintWindow(_vm.Session, _vm.SheetRenderer, _vm.PrintServer, this, _env.PrintHistory) { Owner = Front };
        bool sent = win.ShowDialog() == true;
        return win.UseDriverInstead ? ShowPrintDialog() : sent;
    }

    public (LabelTemplate Template, bool Save)? ShowThermalLabel()
    {
        var win = new ThermalLabelWindow { Owner = Front };
        return win.ShowDialog() == true && win.Result is { } roll ? (roll, win.SaveAsTemplate) : null;
    }

    public LabelMaker.Core.Printing.ReprintRequest? ShowPrintHistory()
    {
        var win = new PrintHistoryWindow(_env.PrintHistory, _vm?.Session.Project.Metadata.Id ?? string.Empty)
        {
            // Opened from the print dialog too, so sit over whichever window is in front.
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? _owner,
        };
        return win.ShowDialog() == true ? win.Result : null;
    }

    public bool ShowApplyTextStyle(LabelMaker.Core.Model.Objects.TextObject source)
    {
        if (_vm is null)
        {
            return false;
        }

        var win = new ApplyTextStyleWindow(_vm.Session, source) { Owner = Front };
        return win.ShowDialog() == true;
    }

    public bool ShowApplyLayout()
    {
        if (_vm is null)
        {
            return false;
        }

        return new ApplyLayoutWindow(_vm.Session) { Owner = Front }.ShowDialog() == true;
    }

    public bool ShowLabelSize()
    {
        if (_vm is null)
        {
            return false;
        }

        var win = new LabelSizeWindow(_vm.Session, _env.Settings.DisplayUnit, _env.Templates, _env.Settings.FavoriteTemplateIds) { Owner = _owner };
        return win.ShowDialog() == true;
    }

    public (double X, double Y, double Width, double Height)? ShowImageCrop(LabelMaker.Core.Model.Objects.ImageObject image)
    {
        if (_vm is null)
        {
            return null;
        }

        if (_vm.SheetRenderer.Images.Get(image.AssetId) is not Rendering.WpfDecodedImage decoded)
        {
            Warn("That image could not be decoded, so it cannot be cropped.");
            return null;
        }

        var win = new ImageCropWindow(image, decoded.Original) { Owner = _owner };
        if (win.ShowDialog() != true)
        {
            return null;
        }

        System.Windows.Rect r = win.Result;
        return (r.X, r.Y, r.Width, r.Height);
    }

    public LabelMaker.Core.Model.ColorRgba? PickColor(LabelMaker.Core.Model.ColorRgba initial, string title)
    {
        var win = new ColorPickerWindow(initial, title, _env.Settings.RecentColors) { Owner = _owner };
        if (win.ShowDialog() != true)
        {
            return null;
        }

        _env.SaveSettings();
        return win.Result;
    }
}
