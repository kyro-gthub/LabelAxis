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

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppEnvironment _env;
    private readonly IShellServices _shell;
    private WpfImageSource _imageSource;
    private readonly SheetVisualRenderer _sheetRenderer;
    private readonly WpfImageExporter _imageExporter;
    private readonly WpfPrintServer _printServer;

    private DocumentSession _session = null!;
    private SelectionProperties _properties = null!;
    private DesignerCanvas? _canvas;

    private string _statusText = "Ready";
    private string _searchText = string.Empty;
    private SearchListItem? _selectedSearchResult;
    private PageListItem? _selectedPage;
    private bool _suppressPageSelection;
    private int _selectedLeftTab = 1;

    public MainViewModel(AppEnvironment env, IShellServices shell)
    {
        _env = env;
        _shell = shell;
        _imageSource = new WpfImageSource(new AssetStore());
        _sheetRenderer = new SheetVisualRenderer(new WpfTextMeasurer(), _imageSource);
        _imageExporter = new WpfImageExporter(_sheetRenderer);
        _printServer = new WpfPrintServer(_sheetRenderer, _env.Calibration);

        BuildCommands();
        RefreshRecent();
        LoadSession(CreateBlankDocument());
    }

    // ---------- session ----------

    public DocumentSession Session => _session;

    public SelectionProperties Properties
    {
        get => _properties;
        private set => SetField(ref _properties, value);
    }

    public SheetVisualRenderer SheetRenderer => _sheetRenderer;

    /// <summary>Shared with the print preview, so preview and print use one paginator.</summary>
    public WpfPrintServer PrintServer => _printServer;

    public void AttachCanvas(DesignerCanvas canvas)
    {
        _canvas = canvas;
        _canvas.Settings = _env.Settings;
        _canvas.Attach(_session);
        _canvas.ZoomChanged += (_, _) => OnPropertyChanged(nameof(ZoomText));
        _canvas.ImagesDropped += (_, e) => OnImagesDroppedOnCanvas(e.Paths, e.AtMm);
    }

    private ProjectDocument CreateBlankDocument()
    {
        LabelTemplate template = _env.Templates.Get(_env.Settings.LastTemplateId ?? "formtec-3100")
                                 ?? _env.Templates.All.First();
        ProjectModel project = ProjectModel.CreateNew(template, "Untitled");
        SeedStarterLabel(project);
        return new ProjectDocument { Project = project };
    }

    private static void SeedStarterLabel(ProjectModel project)
    {
        var doc = project.MasterLabel;
        double w = doc.SizeMm.Width;
        double h = doc.SizeMm.Height;
        doc.Objects.Add(new TextObject
        {
            Name = "Title",
            Bounds = new RectMm(2, 2, Math.Max(10, w - 4), Math.Min(8, h / 3)),
            Content = RichTextContent.FromPlainText(Localization.Localizer.T("Product name")),
            FontSizePt = 10,
            Bold = true,
        });
        doc.Objects.Add(new BarcodeObject
        {
            Name = "Barcode",
            Bounds = new RectMm(2, Math.Min(11, h / 2), Math.Max(20, w - 4), Math.Min(12, h / 2)),
            AutoSize = false,
            Spec = new BarcodeSpec { Symbology = BarcodeSymbology.Code128, Data = "1234567890", ModuleWidthMm = 0.3, BarHeightMm = Math.Min(10, h / 3) },
        });
    }

    /// <summary>
    /// A saved project carries a snapshot of the records it was designed with; bring
    /// them back on open so the preview, record navigator and printing work straight
    /// away, without re-importing the source file.
    /// </summary>
    private void RestoreCachedData(DocumentSession session, ProjectDocument document)
    {
        if (document.DataCaches.Count == 0)
        {
            return;
        }

        ProjectModel project = document.Project;
        ProjectDataCache cache = document.DataCaches.FirstOrDefault(c => c.ConnectionId == project.ActiveDataConnectionId)
                                 ?? document.DataCaches[0];
        DataConnectionModel connection = project.DataConnections.FirstOrDefault(c => c.Id == cache.ConnectionId)
                                         ?? new DataConnectionModel { Kind = DataSourceKind.Csv };
        try
        {
            var service = new DataImportService();
            RawTable raw = service.ReadRaw(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(cache.Csv)), DataSourceKind.Csv,
                new TabularReadOptions { Delimiter = "," });

            // The snapshot is already the built table (header row first, mapped names),
            // so read it back as plain CSV rather than re-applying the source's options.
            var snapshot = new DataConnectionModel { Id = connection.Id, Name = connection.Name, Kind = DataSourceKind.Csv };
            session.SetData(service.BuildDataSet(raw, snapshot), null, markDirty: false);
            DescribeRestoredData(project.DataConnections.FirstOrDefault(c => c.Id == cache.ConnectionId));
        }
        catch (Exception ex)
        {
            StatusText = "Saved records could not be restored: " + ex.Message;
        }
    }

    private void OnActivePageChanged()
    {
        RebuildPages();
        RebuildObjectList();
        _canvas?.FitToView();
        RefreshCanvas();
        RaiseAllCanExecute();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ActivePageName));
        OnPropertyChanged(nameof(LockGuides));
        _properties.RefreshAll();
    }

    // ---------- bindable state ----------

    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

    public string Title => (_session.Document.HasUnsavedChanges ? "• " : string.Empty) +
        (_session.Document.FilePath is { } p ? Path.GetFileName(p) : _session.Project.Metadata.Title) +
        " — LabelAxis";

    public string SelectionInfo
    {
        get
        {
            if (_session.Selection.Count == 0)
            {
                return $"{_session.ActiveLabel.Objects.Count} object(s)";
            }

            if (_session.Selection.Count == 1)
            {
                RectMm b = _session.Selection[0].Bounds;
                return $"X {b.X:0.00}  Y {b.Y:0.00}  W {b.Width:0.00}  H {b.Height:0.00} mm";
            }

            return $"{_session.Selection.Count} selected";
        }
    }

    /// <summary>Units offered for every length in the UI. Storage stays millimetres.</summary>
    public IReadOnlyList<Core.Units.Unit> DisplayUnits { get; } = new[]
    {
        Core.Units.Unit.Mm, Core.Units.Unit.Cm, Core.Units.Unit.Inch, Core.Units.Unit.Point, Core.Units.Unit.Pixel,
    };

    public Core.Units.Unit DisplayUnit
    {
        get => _env.Settings.DisplayUnit;
        set
        {
            if (_env.Settings.DisplayUnit == value)
            {
                return;
            }

            _env.Settings.DisplayUnit = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UnitSymbol));
            _properties.NotifyUnitChanged();
            _canvas?.InvalidateVisual();
        }
    }

    public string UnitSymbol => _env.Settings.DisplayUnit.Symbol();

    public string RecordText => _session.RecordCount > 0
        ? $"{_session.RecordIndex + 1} / {_session.RecordCount}"
        : "— / —";

    public string ZoomText => _canvas is null ? "100%" : $"{Math.Round(_canvas.Zoom * 100)}%";

    /// <summary>Left panel: 1 = Pages and objects, 2 = Data (0 was the template browser, now in the Label manager).</summary>
    public int SelectedLeftTab { get => _selectedLeftTab; set => SetField(ref _selectedLeftTab, value); }

    private int _selectedRightTab;

    /// <summary>Right panel: 0 = Properties, 1 = Layers, 2 = Page.</summary>
    public int SelectedRightTab { get => _selectedRightTab; set => SetField(ref _selectedRightTab, value); }

    private int _selectedDataTab;

    /// <summary>Bottom panel: 0 = Data Table, 1 = Print Queue, 2 = Variable Data.</summary>
    public int SelectedDataTab { get => _selectedDataTab; set => SetField(ref _selectedDataTab, value); }

    public bool HasData => _session.Data is { RowCount: > 0 };

    public string PrintQueueSummary
    {
        get
        {
            string printer = PrinterName;
            string sheet = _session.Project.Template is { } t
                ? $"{t.Grid.Columns * t.Grid.Rows} labels per sheet ({t.Grid.Columns} × {t.Grid.Rows})"
                : "no template";
            return _session.RecordCount > 0
                ? $"{_session.RecordCount:N0} records queued · {sheet} · printing to {printer}."
                : $"No variable data loaded. The master label will fill the sheet — {sheet} · printing to {printer}.";
        }
    }

    public string DataSourceSummary
    {
        get
        {
            if (_session.RecordCount == 0)
            {
                return "No data source connected";
            }

            string records = $"{_session.RecordCount:N0} record{(_session.RecordCount == 1 ? "" : "s")}";
            if (_session.ActiveConnection is not { } connection)
            {
                return records + " loaded";
            }

            string read = connection.LastRefreshedUtc is { } when
                ? $"\nRead {when.ToLocalTime():d MMM yyyy, HH:mm}"
                : string.Empty;
            return $"{records} · {connection.Describe()}{read}";
        }
    }

    // ---------- shell chrome (title bar, tab strip, status bar) ----------

    /// <summary>File name for the document tab, with the unsaved-changes marker.</summary>
    public string DocumentTabTitle =>
        (_session.Document.FilePath is { } p ? Path.GetFileName(p) : "Untitled.labelproj") +
        (_session.Document.HasUnsavedChanges ? "*" : string.Empty);

    public string SaveStateText => _session.Document.HasUnsavedChanges ? "Unsaved changes" : "Project saved";

    public bool HasUnsavedChanges => _session.Document.HasUnsavedChanges;

    /// <summary>"Formtec 3100 (99.1 × 38.1 mm)" for the status bar.</summary>
    public string SheetSummary
    {
        get
        {
            if (_session.Project.Template is not { } t)
            {
                return "No template";
            }

            return $"{t.Brand} {t.ProductCode} ({t.Grid.LabelWidthMm:0.##} × {t.Grid.LabelHeightMm:0.##} mm)";
        }
    }

    public string PrinterName => _env.Settings.LastPrinterName ?? "System default printer";

    public string CalibrationStatus =>
        _env.Settings.LastPrinterName is { } printer && _env.Calibration.Has(printer)
            ? "Calibration: aligned"
            : "Calibration: not set";

    public string RecordsReadyText => _session.RecordCount > 0
        ? $"{_session.RecordCount:N0} records ready to print"
        : "No variable data";

    /// <summary>Switches the project to another label sheet. Also used by the Label manager's "Use this sheet".</summary>
    public void ApplyTemplate(LabelTemplate template)
    {
        ProjectModel project = _session.Project;
        var size = new SizeMm(template.Grid.LabelWidthMm, template.Grid.LabelHeightMm);

        // Undoable like any other edit: sheet and label size change together, and
        // the canvas refits whichever way the step is taken.
        _session.Execute(new CompositeCommand("Change label sheet", new IUndoableCommand[]
        {
            new SetPropertyCommand<LabelTemplate?>("Change sheet", () => project.Template, v => project.Template = v, template),
            new SetPropertyCommand<SizeMm>("Label size", () => project.MasterLabel.SizeMm,
                v => { project.MasterLabel.SizeMm = v; OnSheetChanged(); }, size),
        }));

        _env.Settings.LastTemplateId = template.Id;
        StatusText = $"Template: {template.Brand} {template.ProductCode} — {template.Name}";
    }

    /// <summary>Anything that depends on the sheet or label size, refreshed after the change or its undo.</summary>
    private void OnSheetChanged()
    {
        _canvas?.FitToView();
        RefreshCanvas();
        OnPropertyChanged(nameof(TemplateSummary));
        OnPropertyChanged(nameof(SheetSummary));
    }

    public string TemplateSummary
    {
        get
        {
            LabelTemplate? t = _session.Project.Template;
            if (t is null)
            {
                return "No template";
            }

            return $"{t.Brand} {t.ProductCode} — {t.Name}\n{t.Grid.Columns}×{t.Grid.Rows} · " +
                   $"label {t.Grid.LabelWidthMm:0.##}×{t.Grid.LabelHeightMm:0.##} mm · {t.EffectivePage.Name}";
        }
    }

    public bool ShowGrid
    {
        get => _env.Settings.ShowGrid;
        set { _env.Settings.ShowGrid = value; OnPropertyChanged(); RefreshCanvas(); }
    }

    public bool SnapToGrid
    {
        get => _env.Settings.SnapToGrid;
        set { _env.Settings.SnapToGrid = value; OnPropertyChanged(); }
    }

    public bool ShowRulers
    {
        get => _env.Settings.ShowRulers;
        set { _env.Settings.ShowRulers = value; OnPropertyChanged(); _canvas?.FitToView(); RefreshCanvas(); }
    }

    public bool ShowGuides
    {
        get => _env.Settings.ShowGuides;
        set { _env.Settings.ShowGuides = value; OnPropertyChanged(); RefreshCanvas(); }
    }

    public bool SnapToGuides
    {
        get => _env.Settings.SnapToGuides;
        set { _env.Settings.SnapToGuides = value; OnPropertyChanged(); }
    }

    public bool SnapToObjects
    {
        get => _env.Settings.SnapToObjects;
        set { _env.Settings.SnapToObjects = value; OnPropertyChanged(); }
    }

    public bool ReduceMotion
    {
        get => _env.Settings.ReduceMotion;
        set { _env.Settings.ReduceMotion = value; OnPropertyChanged(); }
    }

    /// <summary>Raised after the Settings dialog changes auto-save preferences, so the running timer can be re-applied.</summary>
    public event EventHandler? AutoSaveSettingsChanged;

    /// <summary>Raised when the dataset is replaced, so the data table can rebuild its columns.</summary>
    public event EventHandler? DataChanged;

    public void NotifyAutoSaveSettingsChanged() => AutoSaveSettingsChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Switches the live color palette (Settings > Appearance) and repaints the canvas, whose
    /// rulers/workspace chrome are drawn with theme-aware colors that aren't resource-bound.</summary>
    public void ApplyTheme(bool dark)
    {
        _env.Settings.DarkTheme = dark;
        Services.ThemeManager.Apply(dark ? Services.AppTheme.Dark : Services.AppTheme.Light);
        RefreshCanvas();
    }

    public bool LockGuides
    {
        get => _session.ActiveGuides.Locked;
        set
        {
            GuideCollection guides = _session.ActiveGuides;
            _session.Execute(new SetPropertyCommand<bool>(
                value ? "Lock guides" : "Unlock guides",
                () => guides.Locked,
                v => { guides.Locked = v; OnPropertyChanged(nameof(LockGuides)); },
                value));
        }
    }

    public ObservableCollection<RecentEntry> RecentProjects { get; } = new();

    public ObservableCollection<ObjectListItem> ObjectList { get; } = new();

    public ObservableCollection<SearchListItem> SearchResults { get; } = new();

    public ObservableCollection<PageListItem> Pages { get; } = new();

    public PageListItem? SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (SetField(ref _selectedPage, value) && value is not null && !_suppressPageSelection)
            {
                _session.SelectPage(value.PageId);
            }
        }
    }

    public string ActivePageName => _session.IsEditingMaster
        ? MasterPageName
        : _session.Project.Pages.FirstOrDefault(p => p.Id == _session.ActivePageId)?.Name ?? "Page";

    /// <summary>The master label's own display name, which is renamable like any page.</summary>
    private string MasterPageName =>
        string.IsNullOrWhiteSpace(_session.Project.MasterLabel.Name) ? "Master label" : _session.Project.MasterLabel.Name;

    public string SearchText
    {
        get => _searchText;
        set { if (SetField(ref _searchText, value)) RefreshSearch(); }
    }

    public SearchListItem? SelectedSearchResult
    {
        get => _selectedSearchResult;
        set
        {
            if (SetField(ref _selectedSearchResult, value) && value is not null)
            {
                NavigateToSearchResult(value);
            }
        }
    }

}

public sealed record RecentEntry(string Path, string Title);
