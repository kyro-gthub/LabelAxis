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

    public void Info(string message, string title = "Label Maker") =>
        MessageBox.Show(_owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void Warn(string message, string title = "Label Maker") =>
        MessageBox.Show(_owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public bool Confirm(string message, string title = "Label Maker") =>
        MessageBox.Show(_owner, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    public bool? ConfirmSaveChanges(string projectTitle)
    {
        MessageBoxResult result = MessageBox.Show(
            _owner,
            $"Save changes to \"{projectTitle}\" before continuing?",
            "Label Maker",
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
        var win = new NewProjectWindow(_env.Templates) { Owner = _owner };
        if (win.ShowDialog() == true && win.SelectedTemplate is { } template)
        {
            onChosen(template);
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

        var win = new PrintSettingsWindow(_vm.Session, this) { Owner = _owner };
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

    public void ShowTemplateManager() =>
        new TemplateManagerWindow(_env) { Owner = _owner }.ShowDialog();

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
            new PrintPreviewWindow(_vm.Session, _vm.PrintServer, this) { Owner = _owner }.ShowDialog();
        }
        catch (Exception ex)
        {
            Warn("Could not build the preview:\n\n" + ex.Message);
        }
    }

    public bool ShowLabelSize()
    {
        if (_vm is null)
        {
            return false;
        }

        var win = new LabelSizeWindow(_vm.Session, _env.Settings.DisplayUnit) { Owner = _owner };
        return win.ShowDialog() == true;
    }

    public bool ShowImageCrop()
    {
        if (_vm is null || _vm.Session.Selection.FirstOrDefault() is not LabelMaker.Core.Model.Objects.ImageObject image)
        {
            return false;
        }

        if (_vm.SheetRenderer.Images.Get(image.AssetId) is not Rendering.WpfDecodedImage decoded)
        {
            Warn("That image could not be decoded, so it cannot be cropped.");
            return false;
        }

        var win = new ImageCropWindow(image, decoded.Original) { Owner = _owner };
        return win.ShowDialog() == true;
    }
}
