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
using LabelMaker.Core.Diagnostics;
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

    /// <summary>New Project: a ready-made design, or a blank label sheet (<paramref name="designsFirst"/> false opens on those).</summary>
    public void NewProject(bool designsFirst = true) =>
        _shell.ShowNewProject(NewProjectFromTemplate, NewProjectFromDesign, designsFirst);

    /// <summary>A new project on <paramref name="template"/>, in its own tab.</summary>
    public void NewProjectFromTemplate(LabelTemplate template)
    {
        _env.Settings.LastTemplateId = template.Id;
        ProjectModel project = ProjectModel.CreateNew(template, "Untitled");
        SeedStarterLabel(project);
        LoadSession(new ProjectDocument { Project = project });
        StatusText = $"New project · {template.Name}";
    }

    private void NewFromDesign()
    {
        if (_shell.ShowDesignGallery() is { } choice)
        {
            NewProjectFromDesign(choice.Design, choice.UseDataFields);
        }
    }

    /// <summary>
    /// A new project from a ready-made design, in its own tab: the design's sheet or roll, its
    /// objects, and grey boxes for the pictures to replace.
    /// </summary>
    public void NewProjectFromDesign(LabelMaker.Core.Designs.LabelDesign design, bool useDataFields)
    {
        if (design.IsMine)
        {
            NewProjectFromMyDesign(design);
            return;
        }

        ProjectDocument document = LabelMaker.Core.Designs.DesignBuilder.Build(design, _env.Templates, new LabelMaker.Core.Designs.DesignBuildOptions
        {
            UseDataFields = useDataFields,
            Measurer = new Rendering.WpfTextMeasurer(),
            Placeholder = Rendering.DesignPlaceholders.Make,
        });
        string name = Localization.Localizer.T(design.Name);
        document.Project.Metadata.Title = name;
        _env.Settings.LastTemplateId = document.Project.Template?.Id;
        LoadSession(document);
        StatusText = Localization.Localizer.F("New project from the design “{0}”", name);
    }

    /// <summary>A new project from one of the user's own designs, exactly as it was saved.</summary>
    private void NewProjectFromMyDesign(LabelMaker.Core.Designs.LabelDesign design)
    {
        try
        {
            ProjectDocument document = LabelMaker.Core.Designs.MyDesigns.Open(design);
            Rendering.CustomFonts.InstallFrom(document, IsInstalledInWindows);
            _env.Settings.LastTemplateId = document.Project.Template?.Id;
            LoadSession(document);
            StatusText = Localization.Localizer.F("New project from your design “{0}”", design.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or
                                       System.Text.Json.JsonException or InvalidOperationException)
        {
            _shell.Warn(Localization.Localizer.T("The design could not be opened:") + "\n\n" + ex.Message);
        }
    }

    /// <summary>
    /// Saves the page on screen as one of the user's designs, to start new labels from: it is
    /// listed first in File › New Project under My designs.
    /// </summary>
    private void SaveActivePageAsDesign()
    {
        LabelDocument page = _session.ActiveLabel;
        string suggested = System.Text.RegularExpressions.Regex.IsMatch(page.Name ?? string.Empty, @"^(Page|Master label)\s*\d*$")
            ? _session.Project.Metadata.Title
            : page.Name!;
        if (suggested is "Untitled" or "")
        {
            suggested = Localization.Localizer.T("My label");
        }

        _session.SyncDataCache();
        Rendering.CustomFonts.EmbedInto(_session.Document);
        if (_shell.AskSaveDesign(_session.Document, page, suggested, name => _env.MyDesigns.Find(name) is not null) is not { } answer)
        {
            return;
        }

        try
        {
            _env.MyDesigns.Save(_session.Document, page, answer.Name, answer.Description, answer.Picture);
            StatusText = Localization.Localizer.F("Saved “{0}” to My designs: it is in File › New Project.", answer.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _shell.Warn(Localization.Localizer.T("The design could not be saved:") + "\n\n" + ex.Message);
        }
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
            // Fonts the project carries that this PC lacks are added first, so it draws in them.
            IReadOnlyList<string> fonts = Rendering.CustomFonts.InstallFrom(doc, IsInstalledInWindows);
            LoadSession(doc);
            _env.Settings.AddRecent(path, doc.Project.Metadata.Title);
            _env.SaveSettings();
            RefreshRecent();
            StatusText = fonts.Count == 0
                ? $"Opened {Path.GetFileName(path)}"
                : Localization.Localizer.F("Opened {0} and added its fonts: {1}", Path.GetFileName(path), string.Join(", ", fonts));
        }
        catch (Exception ex) when (Faults.IsReportable(ex))
        {
            _shell.Warn("Could not open the project:\n\n" + ex.Message);
        }
    }

    /// <summary>
    /// The auto-save timer: a recovery snapshot of every open project with unsaved changes.
    /// Returns how many were written. Recovery is a safety net, so a snapshot that cannot be
    /// written never interrupts work; a mistake in LabelAxis still reaches the crash log.
    /// </summary>
    public int AutoSaveAll()
    {
        int saved = 0;
        foreach (DocumentTab tab in Tabs.Where(t => t.HasUnsavedChanges))
        {
            try
            {
                tab.Session.SyncDataCache();
                _env.AutoSave.Snapshot(tab.Session.Document);
                saved++;
            }
            catch (Exception fault)
            {
                Faults.Survived(fault);
            }
        }

        if (saved > 0)
        {
            StatusText = saved == 1
                ? $"Auto-saved recovery snapshot at {DateTime.Now:T}"
                : $"Auto-saved {saved} projects at {DateTime.Now:T}";
        }

        return saved;
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
        catch (Exception ex) when (Faults.IsReportable(ex))
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
            Rendering.CustomFonts.EmbedInto(_session.Document);
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
        catch (Exception ex) when (Faults.IsReportable(ex))
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
