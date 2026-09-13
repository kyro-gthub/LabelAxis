using System.Collections.ObjectModel;
using System.IO;
using LabelMaker.App.Mvvm;
using LabelMaker.App.Rendering;
using LabelMaker.App.Services;
using LabelMaker.Core.Serialization;

namespace LabelMaker.App.ViewModels;

/// <summary>
/// Several projects open at once, one tab each. Only the active tab's session is wired
/// to the window; switching tabs detaches the old one, attaches the new one and keeps
/// each tab's undo history, records, selection and zoom exactly as they were.
/// </summary>
public sealed partial class MainViewModel
{
    private DocumentTab _activeTab = null!;

    public ObservableCollection<DocumentTab> Tabs { get; } = new();

    public DocumentTab ActiveTab
    {
        get => _activeTab;
        set
        {
            if (value is not null)
            {
                ActivateTab(value);
            }
        }
    }

    /// <summary>Raised just before another tab becomes active, so open editors can finish on the old document.</summary>
    public event EventHandler? ActiveTabChanging;

    /// <summary>Raised after another tab became active (the find window follows it).</summary>
    public event EventHandler? ActiveTabChanged;

    public RelayCommand ActivateTabCommand { get; private set; } = null!;

    public RelayCommand CloseTabCommand { get; private set; } = null!;

    public RelayCommand NextTabCommand { get; private set; } = null!;

    public RelayCommand PreviousTabCommand { get; private set; } = null!;

    public RelayCommand CloseOtherTabsCommand { get; private set; } = null!;

    private void BuildTabCommands()
    {
        ActivateTabCommand = new RelayCommand(p => { if (p is DocumentTab tab) ActivateTab(tab); }, _ => true);
        CloseTabCommand = new RelayCommand(p => CloseTab(p as DocumentTab ?? _activeTab), _ => true);
        NextTabCommand = new RelayCommand(() => StepTab(+1), () => Tabs.Count > 1);
        PreviousTabCommand = new RelayCommand(() => StepTab(-1), () => Tabs.Count > 1);
        CloseOtherTabsCommand = new RelayCommand(
            p =>
            {
                DocumentTab keep = p as DocumentTab ?? _activeTab;
                foreach (DocumentTab tab in Tabs.Where(t => t != keep).ToList())
                {
                    if (!CloseTab(tab))
                    {
                        break;
                    }
                }

                ActivateTab(keep);
            },
            _ => Tabs.Count > 1);
    }

    /// <summary>
    /// Opens <paramref name="document"/> in a tab and shows it. An untouched blank project
    /// in the active tab is replaced rather than left behind.
    /// </summary>
    public void LoadSession(ProjectDocument document)
    {
        DocumentTab tab = CreateTab(document);
        if (_activeTab is { IsPristine: true } blank && Tabs.Contains(blank))
        {
            int index = Tabs.IndexOf(blank);
            Tabs.Insert(index, tab);
            ActivateTab(tab);
            Tabs.Remove(blank);
        }
        else
        {
            Tabs.Add(tab);
            ActivateTab(tab);
        }

        RaiseAllCanExecute();
    }

    private DocumentTab CreateTab(ProjectDocument document)
    {
        var images = new WpfImageSource(document.Assets);
        var session = new DocumentSession(document, images);
        var tab = new DocumentTab(session, images, new SelectionProperties(session, _env.Settings));
        RestoreCachedData(session, document);
        return tab;
    }

    /// <summary>The tab showing the project saved at <paramref name="path"/>, if it is open.</summary>
    private DocumentTab? TabForPath(string path) =>
        Tabs.FirstOrDefault(t => t.Session.Document.FilePath is { } p &&
                                 string.Equals(Path.GetFullPath(p), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));

    private DocumentTab? TabForProject(string projectId) =>
        Tabs.FirstOrDefault(t => t.Session.Project.Metadata.Id == projectId);

    public void ActivateTab(DocumentTab tab)
    {
        if (ReferenceEquals(tab, _activeTab) && ReferenceEquals(_session, tab.Session))
        {
            return;
        }

        if (_activeTab is not null)
        {
            ActiveTabChanging?.Invoke(this, EventArgs.Empty);
            _activeTab.View = _canvas?.CaptureView();
            Unsubscribe(_activeTab.Session);
            _activeTab.IsActive = false;
        }

        _activeTab = tab;
        tab.IsActive = true;
        _imageSource = tab.Images;
        _sheetRenderer.Images = _imageSource;
        _session = tab.Session;
        Subscribe(_session);
        Properties = tab.Properties;
        _canvas?.Attach(_session, tab.View);

        RebuildPages();
        RebuildObjectList();
        RefreshSearch();
        RebuildTemplateCategories();
        OnDocumentChanged();
        _properties.RefreshAll();
        foreach (string name in new[]
                 {
                     nameof(ActiveTab), nameof(PrinterName), nameof(CalibrationStatus), nameof(RecordsReadyText),
                     nameof(TemplateSummary), nameof(LockGuides), nameof(RecordText), nameof(DataSourceSummary),
                     nameof(HasData), nameof(CanRefreshData), nameof(ActivePageName), nameof(ZoomText), nameof(SelectionInfo),
                 })
        {
            OnPropertyChanged(name);
        }

        DataChanged?.Invoke(this, EventArgs.Empty);
        ActiveTabChanged?.Invoke(this, EventArgs.Empty);
    }

    private void StepTab(int direction)
    {
        if (Tabs.Count < 2)
        {
            return;
        }

        int index = (Tabs.IndexOf(_activeTab) + direction + Tabs.Count) % Tabs.Count;
        ActivateTab(Tabs[index]);
    }

    /// <summary>Closes a tab, asking to save first. The last tab is replaced by a blank project.</summary>
    public bool CloseTab(DocumentTab tab)
    {
        if (!Tabs.Contains(tab) || !ConfirmDiscard(tab))
        {
            return false;
        }

        _env.AutoSave.ClearFor(tab.Session.Project.Metadata.Id);
        int index = Tabs.IndexOf(tab);

        if (Tabs.Count == 1)
        {
            DocumentTab blank = CreateTab(CreateBlankDocument());
            Tabs.Add(blank);
            ActivateTab(blank);
        }
        else if (ReferenceEquals(tab, _activeTab))
        {
            ActivateTab(Tabs[index == Tabs.Count - 1 ? index - 1 : index + 1]);
        }

        Tabs.Remove(tab);
        RaiseAllCanExecute();
        StatusText = $"Closed {tab.Title}";
        return true;
    }

    /// <summary>Asks about every tab with unsaved changes, showing each in turn. False if the user cancels.</summary>
    public bool ConfirmCloseAll()
    {
        foreach (DocumentTab tab in Tabs.Where(t => t.HasUnsavedChanges).ToList())
        {
            if (!ConfirmDiscard(tab))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Save-or-discard for one tab; the tab is brought to the front so the user sees what they are deciding about.</summary>
    public bool ConfirmDiscard(DocumentTab tab)
    {
        if (!tab.HasUnsavedChanges)
        {
            return true;
        }

        ActivateTab(tab);
        return ConfirmDiscard();
    }

    // ---- the active session's events -------------------------------------------------

    private void Subscribe(DocumentSession session)
    {
        session.DocumentChanged += Session_DocumentChanged;
        session.SelectionChanged += Session_SelectionChanged;
        session.RecordChanged += Session_RecordChanged;
        session.DataChanged += Session_DataChanged;
        session.RecordsEdited += Session_RecordsEdited;
        session.HistoryChanged += Session_HistoryChanged;
        session.PagesChanged += Session_PagesChanged;
        session.ActivePageChanged += Session_ActivePageChanged;
    }

    private void Unsubscribe(DocumentSession session)
    {
        session.DocumentChanged -= Session_DocumentChanged;
        session.SelectionChanged -= Session_SelectionChanged;
        session.RecordChanged -= Session_RecordChanged;
        session.DataChanged -= Session_DataChanged;
        session.RecordsEdited -= Session_RecordsEdited;
        session.HistoryChanged -= Session_HistoryChanged;
        session.PagesChanged -= Session_PagesChanged;
        session.ActivePageChanged -= Session_ActivePageChanged;
    }

    private void Session_DocumentChanged(object? sender, EventArgs e) => OnDocumentChanged();

    private void Session_SelectionChanged(object? sender, EventArgs e) => OnSelectionChanged();

    private void Session_RecordChanged(object? sender, EventArgs e)
    {
        RefreshCanvas();
        OnPropertyChanged(nameof(RecordText));
    }

    private void Session_DataChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(RecordText));
        OnPropertyChanged(nameof(DataSourceSummary));
        OnPropertyChanged(nameof(RecordsReadyText));
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(CanRefreshData));
        DataChanged?.Invoke(this, EventArgs.Empty);
        RefreshSearch();
    }

    private void Session_RecordsEdited(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(RecordText));
        OnPropertyChanged(nameof(DataSourceSummary));
        OnPropertyChanged(nameof(RecordsReadyText));
        RecordsEdited?.Invoke(this, EventArgs.Empty);
    }

    private void Session_HistoryChanged(object? sender, EventArgs e) => RaiseAllCanExecute();

    private void Session_PagesChanged(object? sender, EventArgs e) => RebuildPages();

    private void Session_ActivePageChanged(object? sender, EventArgs e) => OnActivePageChanged();
}
