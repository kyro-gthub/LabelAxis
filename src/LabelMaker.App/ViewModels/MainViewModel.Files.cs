using System.Collections.ObjectModel;
using System.IO;
using LabelMaker.App.Controls;
using LabelMaker.App.Mvvm;
using LabelMaker.App.Printing;
using LabelMaker.App.Rendering;
using LabelMaker.App.Services;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Bulk;
using LabelMaker.Core.Data;
using LabelMaker.Core.Data.Import;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Preflight;
using LabelMaker.Core.Printing;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Serialization;
using LabelMaker.Core.Units;
using LabelMaker.Core.Variables;
using LabelMaker.Export;
using LabelMaker.Export.Pdf;
using LabelMaker.Export.Svg;

namespace LabelMaker.App.ViewModels;

/// <summary>New, open, save, recent files and recovery.</summary>
public sealed partial class MainViewModel
{
    // ---------- file ----------

    private void NewProject()
    {
        _shell.ShowNewProject(NewProjectFromTemplate);
    }

    /// <summary>A new project on <paramref name="template"/>, in its own tab.</summary>
    public void NewProjectFromTemplate(LabelTemplate template)
    {
        _env.Settings.LastTemplateId = template.Id;
        ProjectModel project = ProjectModel.CreateNew(template, "Untitled");
        SeedStarterLabel(project);
        LoadSession(new ProjectDocument { Project = project });
        StatusText = $"New project · {template.Name}";
    }

    /// <summary>A new project on a thermal roll defined in the thermal label window.</summary>
    private void NewThermalLabel()
    {
        if (_shell.ShowThermalLabel() is not { } choice)
        {
            return;
        }

        if (choice.Save)
        {
            try
            {
                LabelTemplate saved = choice.Template with { IsUserDefined = true };
                _env.Templates.SaveUserTemplate(saved, _env.Paths.UserTemplatesDirectory);
                _env.Templates.AddOrReplace(saved, validate: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                StatusText = "The roll could not be saved to your templates: " + ex.Message;
            }
        }

        NewProjectFromTemplate(choice.Template);
        _activeTab.Session.Project.PrintSettings.Content = PrintContent.SheetFill;
    }

    /// <summary>The welcome window, and whatever the user chose on it.</summary>
    public void ShowWelcome()
    {
        _shell.ShowWelcome(this);
        _env.SaveSettings();
    }

    private void OpenProject()
    {
        string? path = _shell.PickOpenFile("LabelAxis project|*.labelproj|All files|*.*", "Open project");
        if (path is not null)
        {
            OpenProjectPath(path);
        }
    }

    public void OpenProjectPath(string path)
    {
        if (TabForPath(path) is { } open)
        {
            ActivateTab(open);
            StatusText = $"{Path.GetFileName(path)} is already open";
            return;
        }

        try
        {
            ProjectDocument doc = ProjectFile.Load(path);
            LoadSession(doc);
            _env.Settings.AddRecent(path, doc.Project.Metadata.Title);
            _env.SaveSettings();
            RefreshRecent();
            StatusText = $"Opened {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            _shell.Warn("Could not open the project:\n\n" + ex.Message);
        }
    }

    public void RestoreRecovered(RecoveryInfo info)
    {
        try
        {
            LoadSession(_env.AutoSave.Restore(info));
            _session.Document.FilePath = info.OriginalPath;
            _session.Document.HasUnsavedChanges = true;
            StatusText = "Recovered unsaved project";
        }
        catch (Exception ex)
        {
            _shell.Warn("Recovery failed: " + ex.Message);
        }
    }

    private bool Save(bool forceDialog)
    {
        string? path = _session.Document.FilePath;
        if (forceDialog || path is null)
        {
            path = _shell.PickSaveFile("LabelAxis project|*.labelproj", SafeName(_session.Project.Metadata.Title), "Save project");
            if (path is null)
            {
                return false;
            }
        }

        try
        {
            _session.SyncDataCache();
            ProjectFile.Save(_session.Document, path);
            _env.Settings.AddRecent(path, _session.Project.Metadata.Title);
            _env.Settings.LastTemplateId = _session.Project.Template?.Id;
            _env.SaveSettings();
            _env.AutoSave.ClearFor(_session.Project.Metadata.Id);
            RefreshRecent();
            _activeTab.RefreshHeader();
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(DocumentTabTitle));
            OnPropertyChanged(nameof(SaveStateText));
            OnPropertyChanged(nameof(HasUnsavedChanges));
            StatusText = $"Saved {Path.GetFileName(path)}";
            return true;
        }
        catch (Exception ex)
        {
            _shell.Warn("Save failed:\n\n" + ex.Message);
            return false;
        }
    }

    public bool ConfirmDiscard()
    {
        if (!_session.Document.HasUnsavedChanges)
        {
            return true;
        }

        bool? choice = _shell.ConfirmSaveChanges(_activeTab.Title);
        return choice switch
        {
            true => Save(false),
            false => true,
            _ => false,
        };
    }
}
