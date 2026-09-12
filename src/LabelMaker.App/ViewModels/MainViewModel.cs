using System.Collections.ObjectModel;
using System.IO;
using LabelMaker.App.Controls;
using LabelMaker.App.Mvvm;
using LabelMaker.App.Printing;
using LabelMaker.App.Rendering;
using LabelMaker.App.Services;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Data;
using LabelMaker.Core.Data.Import;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Preflight;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Serialization;
using LabelMaker.Core.Units;
using LabelMaker.Core.Variables;
using LabelMaker.Export;
using LabelMaker.Export.Pdf;
using LabelMaker.Export.Svg;

namespace LabelMaker.App.ViewModels;

public sealed class MainViewModel : ObservableObject
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
    private ObjectListItem? _selectedObjectItem;
    private PageListItem? _selectedPage;
    private bool _suppressPageSelection;
    private bool _hasConfiguredPrintSettings;
    private int _selectedLeftTab;

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
            Content = RichTextContent.FromPlainText("Product name"),
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

    public void LoadSession(ProjectDocument document)
    {
        _imageSource = new WpfImageSource(document.Assets);
        _sheetRenderer.Images = _imageSource;

        _session = new DocumentSession(document, _imageSource);
        _session.DocumentChanged += (_, _) => OnDocumentChanged();
        _session.SelectionChanged += (_, _) => OnSelectionChanged();
        _session.RecordChanged += (_, _) => { RefreshCanvas(); OnPropertyChanged(nameof(RecordText)); };
        _session.DataChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(RecordText));
            OnPropertyChanged(nameof(DataSourceSummary));
            OnPropertyChanged(nameof(RecordsReadyText));
            DataChanged?.Invoke(this, EventArgs.Empty);
            RefreshSearch();
        };
        _session.HistoryChanged += (_, _) => RaiseAllCanExecute();
        _session.PagesChanged += (_, _) => RebuildPages();
        _session.ActivePageChanged += (_, _) => OnActivePageChanged();

        Properties = new SelectionProperties(_session, _env.Settings);
        _canvas?.Attach(_session);

        RebuildPages();
        RebuildObjectList();
        RefreshSearch();
        RebuildTemplateCategories();
        OnDocumentChanged();
        OnPropertyChanged(nameof(PrinterName));
        OnPropertyChanged(nameof(CalibrationStatus));
        OnPropertyChanged(nameof(RecordsReadyText));
        OnPropertyChanged(nameof(TemplateSummary));
        OnPropertyChanged(nameof(LockGuides));
        OnPropertyChanged(nameof(RecordText));
        OnPropertyChanged(nameof(DataSourceSummary));
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

    private void RebuildPages()
    {
        _suppressPageSelection = true;
        Pages.Clear();
        Pages.Add(new PageListItem(null, MasterPageName, 0, IsMaster: true));
        for (int i = 0; i < _session.Project.Pages.Count; i++)
        {
            LabelPage p = _session.Project.Pages[i];
            Pages.Add(new PageListItem(p.Id, p.Name, i + 1, IsMaster: false));
        }

        _selectedPage = Pages.FirstOrDefault(p => p.PageId == _session.ActivePageId) ?? Pages[0];
        OnPropertyChanged(nameof(SelectedPage));
        _suppressPageSelection = false;
    }

    // ---------- bindable state ----------

    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

    public string Title => (_session.Document.HasUnsavedChanges ? "• " : string.Empty) +
        (_session.Document.FilePath is { } p ? Path.GetFileName(p) : _session.Project.Metadata.Title) +
        " — Label Maker";

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

    /// <summary>Left panel: 0 = Templates, 1 = Assets, 2 = Data.</summary>
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

    private string _templateSearchText = string.Empty;

    /// <summary>Filters the template list in the left panel.</summary>
    public string TemplateSearchText
    {
        get => _templateSearchText;
        set
        {
            if (SetField(ref _templateSearchText, value))
            {
                RebuildTemplateCategories();
            }
        }
    }

    public string DataSourceSummary => _session.RecordCount > 0
        ? $"{_session.RecordCount} record{(_session.RecordCount == 1 ? "" : "s")} loaded"
        : "No data source connected";

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

            return $"{t.Brand} {t.ProductCode} ({t.Grid.LabelWidthMm:0.#} × {t.Grid.LabelHeightMm:0.#} mm)";
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

    public ObservableCollection<TemplateCategoryItem> TemplateCategories { get; } = new();

    private TemplateCategoryItem? _selectedCategory;

    public TemplateCategoryItem? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetField(ref _selectedCategory, value) && value is not null)
            {
                TemplateResults.Clear();
                foreach (LabelTemplate t in value.Templates.Take(60))
                {
                    TemplateResults.Add(new TemplateListItem(t));
                }
            }
        }
    }

    /// <summary>Templates inside the selected left-panel category.</summary>
    public ObservableCollection<TemplateListItem> TemplateResults { get; } = new();

    private TemplateListItem? _selectedTemplateResult;

    public TemplateListItem? SelectedTemplateResult
    {
        get => _selectedTemplateResult;
        set
        {
            if (SetField(ref _selectedTemplateResult, value) && value is not null)
            {
                ApplyTemplate(value.Template);
            }
        }
    }

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

    private void RebuildTemplateCategories()
    {
        TemplateCategories.Clear();
        IEnumerable<LabelTemplate> source = _env.Templates.All;
        if (!string.IsNullOrWhiteSpace(_templateSearchText))
        {
            string q = _templateSearchText.Trim();
            source = source.Where(t =>
                t.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                t.ProductCode.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                t.Brand.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        var all = source.ToList();

        void Add(string title, string subtitle, string icon, Func<LabelTemplate, bool> match)
        {
            var hits = all.Where(match).OrderBy(t => t.ProductCode, StringComparer.OrdinalIgnoreCase).ToList();
            if (hits.Count > 0)
            {
                TemplateCategories.Add(new TemplateCategoryItem(title, subtitle, icon, hits));
            }
        }

        Add("Formtec Templates", "A4, A5 and label sheets", "Icon.Sheet",
            t => t.Brand.Contains("Formtec", StringComparison.OrdinalIgnoreCase));
        Add("Product Labels", "Retail & commercial", "Icon.Tag",
            t => t.Grid.LabelWidthMm >= 50 && t.Grid.LabelHeightMm >= 20);
        Add("Barcode Labels", "Logistics & inventory", "Icon.Barcode",
            t => t.Grid.LabelWidthMm >= 40 && t.Grid.LabelHeightMm < 30);
        Add("Small & Serial", "Sequential numbering", "Icon.Sticker",
            t => t.Grid.LabelWidthMm < 40);
        Add("Round & Special", "Shaped labels", "Icon.Ellipse",
            t => t.Grid.LabelShape != LabelShape.Rectangle);
        Add("All Templates", "Every installed sheet", "Icon.Gallery", _ => true);

        _selectedCategory = TemplateCategories.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedCategory));
        if (_selectedCategory is not null)
        {
            TemplateResults.Clear();
            foreach (LabelTemplate t in _selectedCategory.Templates.Take(60))
            {
                TemplateResults.Add(new TemplateListItem(t));
            }
        }
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
                   $"label {t.Grid.LabelWidthMm:0.#}×{t.Grid.LabelHeightMm:0.#} mm · {t.EffectivePage.Name}";
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
        get => _session.ActiveLabel.Guides.Locked;
        set
        {
            GuideCollection guides = _session.ActiveLabel.Guides;
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

    public ObjectListItem? SelectedObjectItem
    {
        get => _selectedObjectItem;
        set
        {
            if (SetField(ref _selectedObjectItem, value) && value is not null)
            {
                _session.SetSelection(new[] { value.Object });
            }
        }
    }

    // ---------- commands ----------

    public RelayCommand NewCommand { get; private set; } = null!;
    public RelayCommand OpenCommand { get; private set; } = null!;
    public RelayCommand OpenRecentCommand { get; private set; } = null!;
    public RelayCommand SaveCommand { get; private set; } = null!;
    public RelayCommand SaveAsCommand { get; private set; } = null!;
    public RelayCommand ExitCommand { get; private set; } = null!;
    public RelayCommand UndoCommand { get; private set; } = null!;
    public RelayCommand RedoCommand { get; private set; } = null!;
    public RelayCommand DeleteCommand { get; private set; } = null!;
    public RelayCommand DuplicateCommand { get; private set; } = null!;
    public RelayCommand CopyCommand { get; private set; } = null!;
    public RelayCommand CutCommand { get; private set; } = null!;
    public RelayCommand PasteCommand { get; private set; } = null!;
    public RelayCommand PasteHereCommand { get; private set; } = null!;
    public RelayCommand GroupCommand { get; private set; } = null!;
    public RelayCommand UngroupCommand { get; private set; } = null!;
    public RelayCommand AddPageCommand { get; private set; } = null!;
    public RelayCommand DuplicatePageCommand { get; private set; } = null!;
    public RelayCommand RemovePageCommand { get; private set; } = null!;
    public RelayCommand RenamePageCommand { get; private set; } = null!;
    public RelayCommand AddVerticalGuideCommand { get; private set; } = null!;
    public RelayCommand AddHorizontalGuideCommand { get; private set; } = null!;
    public RelayCommand AddTextCommand { get; private set; } = null!;
    public RelayCommand AddBarcodeCommand { get; private set; } = null!;
    public RelayCommand AddQrCodeCommand { get; private set; } = null!;
    public RelayCommand AddImageCommand { get; private set; } = null!;
    public RelayCommand AddRectangleCommand { get; private set; } = null!;
    public RelayCommand AddRoundRectCommand { get; private set; } = null!;
    public RelayCommand AddEllipseCommand { get; private set; } = null!;
    public RelayCommand AddLineCommand { get; private set; } = null!;
    public RelayCommand AddDataFieldCommand { get; private set; } = null!;
    public RelayCommand AddDateFieldCommand { get; private set; } = null!;
    public RelayCommand BringToFrontCommand { get; private set; } = null!;
    public RelayCommand BringForwardCommand { get; private set; } = null!;
    public RelayCommand SendBackwardCommand { get; private set; } = null!;
    public RelayCommand SendToBackCommand { get; private set; } = null!;
    public RelayCommand AlignLeftCommand { get; private set; } = null!;
    public RelayCommand AlignTopCommand { get; private set; } = null!;
    public RelayCommand CenterHorizontalCommand { get; private set; } = null!;
    public RelayCommand CenterVerticalCommand { get; private set; } = null!;
    public RelayCommand ImportDataCommand { get; private set; } = null!;
    public RelayCommand ClearDataCommand { get; private set; } = null!;
    public RelayCommand SequencesCommand { get; private set; } = null!;
    public RelayCommand DuplicateCheckCommand { get; private set; } = null!;
    public RelayCommand BulkBarcodeCommand { get; private set; } = null!;
    public RelayCommand BarcodeImageCommand { get; private set; } = null!;
    public RelayCommand PreflightCommand { get; private set; } = null!;
    public RelayCommand TemplateManagerCommand { get; private set; } = null!;
    public RelayCommand PrintCommand { get; private set; } = null!;
    public RelayCommand PrintSettingsCommand { get; private set; } = null!;
    public RelayCommand PrintPreviewCommand { get; private set; } = null!;
    public RelayCommand CalibrationCommand { get; private set; } = null!;
    public RelayCommand ExportSheetPdfCommand { get; private set; } = null!;
    public RelayCommand ExportLabelPngCommand { get; private set; } = null!;
    public RelayCommand ExportLabelSvgCommand { get; private set; } = null!;
    public RelayCommand ZoomInCommand { get; private set; } = null!;
    public RelayCommand ZoomOutCommand { get; private set; } = null!;
    public RelayCommand ZoomFitCommand { get; private set; } = null!;
    public RelayCommand ZoomActualCommand { get; private set; } = null!;
    public RelayCommand ClearGuidesCommand { get; private set; } = null!;
    public RelayCommand FirstRecordCommand { get; private set; } = null!;
    public RelayCommand PrevRecordCommand { get; private set; } = null!;
    public RelayCommand NextRecordCommand { get; private set; } = null!;
    public RelayCommand LastRecordCommand { get; private set; } = null!;
    public RelayCommand AboutCommand { get; private set; } = null!;
    public RelayCommand SettingsCommand { get; private set; } = null!;
    public RelayCommand ReplaceImageCommand { get; private set; } = null!;
    public RelayCommand ToggleObjectLockCommand { get; private set; } = null!;
    public RelayCommand AddShapeCommand { get; private set; } = null!;
    public RelayCommand DataTableCommand { get; private set; } = null!;
    public RelayCommand LabelSizeCommand { get; private set; } = null!;
    public RelayCommand SelectAllCommand { get; private set; } = null!;
    public RelayCommand CropImageCommand { get; private set; } = null!;
    public RelayCommand ClearCropCommand { get; private set; } = null!;
    public RelayCommand PickColorCommand { get; private set; } = null!;
    public RelayCommand ApplySwatchCommand { get; private set; } = null!;
    public RelayCommand FindCommand { get; private set; } = null!;
    public RelayCommand FindReplaceCommand { get; private set; } = null!;
    public RelayCommand FlipHorizontalCommand { get; private set; } = null!;
    public RelayCommand FlipVerticalCommand { get; private set; } = null!;
    public RelayCommand RotateCommand { get; private set; } = null!;

    private void BuildCommands()
    {
        NewCommand = new RelayCommand(NewProject);
        OpenCommand = new RelayCommand(OpenProject);
        OpenRecentCommand = new RelayCommand(p => { if (p is string s) OpenProjectPath(s); });
        SaveCommand = new RelayCommand(() => Save(false));
        SaveAsCommand = new RelayCommand(() => Save(true));
        ExitCommand = new RelayCommand(() => System.Windows.Application.Current.MainWindow?.Close());
        UndoCommand = new RelayCommand(() => _session.Undo(), () => _session.History.CanUndo);
        RedoCommand = new RelayCommand(() => _session.Redo(), () => _session.History.CanRedo);
        DeleteCommand = new RelayCommand(DeleteSelection, () => _session.Selection.Count > 0);
        DuplicateCommand = new RelayCommand(DuplicateSelection, () => _session.Selection.Count > 0);
        CopyCommand = new RelayCommand(() => ObjectClipboard.Set(_session.Selection), () => _session.Selection.Count > 0);
        CutCommand = new RelayCommand(() => { ObjectClipboard.Set(_session.Selection); DeleteSelection(); }, () => _session.Selection.Count > 0);
        PasteCommand = new RelayCommand(() => PasteObjects(), () => ObjectClipboard.HasContent);
        PasteHereCommand = new RelayCommand(() => PasteObjects(atContextPoint: true), () => ObjectClipboard.HasContent);
        GroupCommand = new RelayCommand(GroupSelection, () => _session.Selection.Count > 1);
        UngroupCommand = new RelayCommand(UngroupSelection, () => _session.Selection.Any(o => o.GroupId is not null));

        AddPageCommand = new RelayCommand(() => _session.AddPage());
        DuplicatePageCommand = new RelayCommand(() => _session.DuplicateActivePage());
        RemovePageCommand = new RelayCommand(
            () => { if (!_env.Settings.ConfirmDestructiveActions || _shell.Confirm($"Remove page \"{ActivePageName}\"?")) _session.RemoveActivePage(); },
            () => !_session.IsEditingMaster);
        RenamePageCommand = new RelayCommand(RenameActivePage);
        AddVerticalGuideCommand = new RelayCommand(() => _canvas?.InsertGuideAtContext(vertical: true));
        AddHorizontalGuideCommand = new RelayCommand(() => _canvas?.InsertGuideAtContext(vertical: false));

        AddTextCommand = new RelayCommand(() => AddObject(new TextObject { Content = RichTextContent.FromPlainText("Text"), Bounds = NewBounds(40, 8) }));
        AddBarcodeCommand = new RelayCommand(() => AddObject(new BarcodeObject
        {
            Bounds = NewBounds(40, 16),
            AutoSize = false,
            Spec = new BarcodeSpec { Symbology = BarcodeSymbology.Code128, Data = "12345678", ModuleWidthMm = 0.3, BarHeightMm = 12 },
        }));
        AddQrCodeCommand = new RelayCommand(() => AddObject(new BarcodeObject
        {
            Name = "QR code",
            Bounds = NewBounds(20, 20),
            AutoSize = false,
            Spec = new BarcodeSpec
            {
                Symbology = BarcodeSymbology.QrCode,
                Data = "https://example.com",
                ModuleWidthMm = 0.6,
                HumanReadable = HumanReadablePosition.None,
            },
        }));
        AddImageCommand = new RelayCommand(AddImage);
        AddRectangleCommand = new RelayCommand(() => AddShape("rectangle"));
        AddRoundRectCommand = new RelayCommand(() => AddShape("round"));
        AddEllipseCommand = new RelayCommand(() => AddShape("ellipse"));
        AddLineCommand = new RelayCommand(() => AddShape("line"));
        AddShapeCommand = new RelayCommand(p => AddShape(p as string ?? "rectangle"));
        AddDataFieldCommand = new RelayCommand(AddDataField);
        AddDateFieldCommand = new RelayCommand(() => AddObject(new TextObject { Name = "Date", Content = RichTextContent.FromPlainText("{CurrentDate}"), Bounds = NewBounds(30, 6) }));

        BringToFrontCommand = new RelayCommand(() => Reorder(int.MaxValue), () => _session.Selection.Count > 0);
        BringForwardCommand = new RelayCommand(() => Reorder(+1), () => _session.Selection.Count > 0);
        SendBackwardCommand = new RelayCommand(() => Reorder(-1), () => _session.Selection.Count > 0);
        SendToBackCommand = new RelayCommand(() => Reorder(int.MinValue), () => _session.Selection.Count > 0);

        AlignLeftCommand = new RelayCommand(() => Align(a => a.X = _session.Selection.Min(s => s.Bounds.X)), MultiSel);
        AlignTopCommand = new RelayCommand(() => Align(a => a.Y = _session.Selection.Min(s => s.Bounds.Y)), MultiSel);
        CenterHorizontalCommand = new RelayCommand(() => Align(a => a.X = (_session.ActiveLabel.SizeMm.Width - a.W) / 2), AnySel);
        CenterVerticalCommand = new RelayCommand(() => Align(a => a.Y = (_session.ActiveLabel.SizeMm.Height - a.H) / 2), AnySel);

        ImportDataCommand = new RelayCommand(ImportData);
        ClearDataCommand = new RelayCommand(
            () => { if (!_env.Settings.ConfirmDestructiveActions || _shell.Confirm("Clear the connected data source?")) _session.SetData(null, null); },
            () => _session.Data is not null);
        SequencesCommand = new RelayCommand(() => _shell.ShowSequenceManager());
        DuplicateCheckCommand = new RelayCommand(DuplicateCheck);
        BulkBarcodeCommand = new RelayCommand(() => _shell.ShowBulkBarcode());
        BarcodeImageCommand = new RelayCommand(() => _shell.ShowBarcodeImage());
        PreflightCommand = new RelayCommand(RunPreflight);
        TemplateManagerCommand = new RelayCommand(() => _shell.ShowTemplateManager());

        PrintCommand = new RelayCommand(Print);
        PrintSettingsCommand = new RelayCommand(OpenPrintSettingsThenPrint);
        PrintPreviewCommand = new RelayCommand(() => _shell.ShowPrintPreview());
        DataTableCommand = new RelayCommand(() => _shell.ShowDataTable());
        SelectAllCommand = new RelayCommand(
            () => _session.SetSelection(_session.ActiveLabel.Objects.Where(o => o.Visible).ToList()),
            () => _session.ActiveLabel.Objects.Count > 0);
        CropImageCommand = new RelayCommand(
            () =>
            {
                if (_session.Selection.FirstOrDefault() is not ImageObject image ||
                    _shell.ShowImageCrop(image) is not { } crop)
                {
                    return;
                }

                ImageAdjustments next = image.Adjustments.Clone();
                next.CropX = crop.X;
                next.CropY = crop.Y;
                next.CropWidth = crop.Width;
                next.CropHeight = crop.Height;
                _session.Execute(new SetPropertyCommand<ImageAdjustments>(
                    "Crop image", () => image.Adjustments, v => image.Adjustments = v, next));
            },
            () => _session.Selection.Count == 1 && _session.Selection[0] is ImageObject);
        ClearCropCommand = new RelayCommand(
            () => { _properties.ClearCrop(); RefreshCanvas(); },
            () => _session.Selection.Count == 1 && _session.Selection[0] is ImageObject);

        // Parameter is "fill" or "stroke": which of the shape's two colours to edit.
        PickColorCommand = new RelayCommand(p =>
        {
            string target = p as string ?? "fill";
            string currentHex = target switch
            {
                "stroke" => _properties.StrokeHex,
                "gradient" => _properties.GradientToHex,
                "text" => _properties.TextColorHex,
                "highlight" => _properties.HighlightHex,
                _ => _properties.FillHex,
            };

            if (!ColorRgba.TryParse(currentHex, out ColorRgba current))
            {
                current = target is "stroke" or "text" ? ColorRgba.Black : ColorRgba.Transparent;
            }

            string title = target switch
            {
                "stroke" => "Stroke colour",
                "gradient" => "Gradient end colour",
                "text" => "Text colour",
                "highlight" => "Highlight colour",
                _ => "Fill colour",
            };

            if (_shell.PickColor(current, title) is { } chosen)
            {
                _properties.ApplySwatch(target, chosen.ToHex());
                RefreshCanvas();
            }
        });

        FindCommand = new RelayCommand(() => _shell.ShowFindReplace(replace: false, initialQuery: null));
        FindReplaceCommand = new RelayCommand(() => _shell.ShowFindReplace(replace: true, initialQuery: null));

        FlipHorizontalCommand = new RelayCommand(() => Flip(horizontal: true), AnySel);
        FlipVerticalCommand = new RelayCommand(() => Flip(horizontal: false), AnySel);

        // Parameter is the angle in degrees, e.g. "90" / "-90" / "180".
        RotateCommand = new RelayCommand(p =>
        {
            double by = double.TryParse(p as string, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 90;
            RotateSelection(by);
        }, _ => AnySel());

        // Parameter is "fill|#RRGGBB" from the palette strip in Shape Settings.
        ApplySwatchCommand = new RelayCommand(p =>
        {
            if (p is string s && s.Split('|') is [string target, string hex])
            {
                _properties.ApplySwatch(target, hex);
                RefreshCanvas();
            }
        });
        LabelSizeCommand = new RelayCommand(() =>
        {
            if (_shell.ShowLabelSize())
            {
                OnPropertyChanged(nameof(TemplateSummary));
                OnPropertyChanged(nameof(SheetSummary));
                _canvas?.FitToView();
                RefreshCanvas();
            }
        });
        CalibrationCommand = new RelayCommand(() => _shell.ShowCalibrationWizard());
        ExportSheetPdfCommand = new RelayCommand(ExportSheetPdf);
        ExportLabelPngCommand = new RelayCommand(ExportLabelPng);
        ExportLabelSvgCommand = new RelayCommand(ExportLabelSvg);

        ZoomInCommand = new RelayCommand(() => _canvas?.ZoomIn());
        ZoomOutCommand = new RelayCommand(() => _canvas?.ZoomOut());
        ZoomFitCommand = new RelayCommand(() => _canvas?.FitToView());
        ZoomActualCommand = new RelayCommand(() => _canvas?.ZoomToActualSize());
        ClearGuidesCommand = new RelayCommand(() => _canvas?.ClearGuides());

        FirstRecordCommand = new RelayCommand(() => _session.RecordIndex = 0);
        PrevRecordCommand = new RelayCommand(() => _session.RecordIndex--);
        NextRecordCommand = new RelayCommand(() => _session.RecordIndex++);
        LastRecordCommand = new RelayCommand(() => _session.RecordIndex = _session.RecordCount - 1);

        AboutCommand = new RelayCommand(() => _shell.Info(
            "Label Maker 1.0\n\nWindows-first label design & printing.\n" +
            "Label sheet templates · barcodes · variable data · accurate printing.\n\n" +
            (_env.IsPortable ? "Portable build — settings and templates live in " : "Settings and templates live in ") +
            _env.Paths.RoamingDirectory,
            "About Label Maker"));
        SettingsCommand = new RelayCommand(() => _shell.ShowSettings());
        ReplaceImageCommand = new RelayCommand(ReplaceSelectedImage, () => Properties.IsImage);
        ToggleObjectLockCommand = new RelayCommand(p => ToggleObjectLock(p as ObjectListItem));
    }

    private bool AnySel() => _session.Selection.Count > 0;
    private bool MultiSel() => _session.Selection.Count > 1;

    // ---------- object operations ----------

    /// <summary>
    /// Centres a new object on the label, shrinking the requested size to fit.
    /// The defaults here are sized for a business-card-ish label; on a 25 x 10 mm
    /// sheet an unclamped 40 x 8 mm text box lands mostly outside the artboard,
    /// which is what "the object doesn't match the canvas" looked like.
    /// </summary>
    private RectMm NewBounds(double w, double h)
    {
        var size = _session.ActiveLabel.SizeMm;

        // Leave a visible margin, and never exceed the label itself.
        double maxW = Math.Max(1, size.Width * 0.84);
        double maxH = Math.Max(0.4, size.Height * 0.72);

        if (w > maxW || h > maxH)
        {
            // Scale both axes by the same factor so the intended proportion survives;
            // a zero/near-zero height (the Line default) must not divide by zero.
            double scale = Math.Min(w > 0 ? maxW / w : 1, h > 0.01 ? maxH / h : 1);
            w *= scale;
            h = h > 0.01 ? h * scale : h;
        }

        return new RectMm(
            Math.Max(0, (size.Width - w) / 2),
            Math.Max(0, (size.Height - h) / 2),
            w,
            h);
    }

    /// <summary>
    /// Every entry in the Shapes and Lines toolbar menus funnels through here. The
    /// model already understands N-gons, stars, arrows and dash patterns; this just
    /// names the useful combinations.
    /// </summary>
    private void AddShape(string kind)
    {
        ShapeObject shape = kind.ToLowerInvariant() switch
        {
            "round" => new ShapeObject { Shape = ShapeKind.RoundRectangle, CornerRadiusMm = 2, Bounds = NewBounds(30, 20) },
            "ellipse" => new ShapeObject { Shape = ShapeKind.Ellipse, Bounds = NewBounds(24, 24) },
            "circle" => new ShapeObject { Shape = ShapeKind.Ellipse, Bounds = NewBounds(24, 24) },
            "triangle" => new ShapeObject { Shape = ShapeKind.Polygon, PolygonPoints = 3, Bounds = NewBounds(24, 24) },
            "diamond" => new ShapeObject { Shape = ShapeKind.Polygon, PolygonPoints = 4, Bounds = NewBounds(24, 24) },
            "pentagon" => new ShapeObject { Shape = ShapeKind.Polygon, PolygonPoints = 5, Bounds = NewBounds(24, 24) },
            "hexagon" => new ShapeObject { Shape = ShapeKind.Polygon, PolygonPoints = 6, Bounds = NewBounds(24, 24) },
            "octagon" => new ShapeObject { Shape = ShapeKind.Polygon, PolygonPoints = 8, Bounds = NewBounds(24, 24) },
            "star5" => new ShapeObject { Shape = ShapeKind.Polygon, PolygonPoints = 5, StarInnerRatio = 0.42, Bounds = NewBounds(24, 24) },
            "star6" => new ShapeObject { Shape = ShapeKind.Polygon, PolygonPoints = 6, StarInnerRatio = 0.5, Bounds = NewBounds(24, 24) },
            "burst" => new ShapeObject { Shape = ShapeKind.Polygon, PolygonPoints = 12, StarInnerRatio = 0.7, Bounds = NewBounds(24, 24) },
            "arrow" => new ShapeObject { Shape = ShapeKind.Arrow, Bounds = NewBounds(40, 0.5), StrokeWidthMm = 0.4 },
            "line" => new ShapeObject { Shape = ShapeKind.Line, Bounds = NewBounds(40, 0.5), StrokeWidthMm = 0.3 },
            "dashed" => new ShapeObject { Shape = ShapeKind.Line, DashStyle = DashStyle.Dash, Bounds = NewBounds(40, 0.5), StrokeWidthMm = 0.3 },
            "dotted" => new ShapeObject { Shape = ShapeKind.Line, DashStyle = DashStyle.Dot, Bounds = NewBounds(40, 0.5), StrokeWidthMm = 0.3 },
            "dashdot" => new ShapeObject { Shape = ShapeKind.Line, DashStyle = DashStyle.DashDot, Bounds = NewBounds(40, 0.5), StrokeWidthMm = 0.3 },
            "hairline" => new ShapeObject { Shape = ShapeKind.Line, Bounds = NewBounds(40, 0.2), StrokeWidthMm = 0.1 },
            "dashedbox" => new ShapeObject { Shape = ShapeKind.Rectangle, DashStyle = DashStyle.Dash, Bounds = NewBounds(30, 20) },
            _ => new ShapeObject { Shape = ShapeKind.Rectangle, Bounds = NewBounds(30, 20) },
        };

        shape.Stroke = ColorRgba.Black;
        if (shape.StrokeWidthMm <= 0)
        {
            shape.StrokeWidthMm = 0.25;
        }

        AddObject(shape);
    }

    /// <summary>
    /// Mirrors the selection. An image flips its own pixels (the model carries flip
    /// flags); everything else mirrors its position inside the selection's bounding
    /// box, which is what flipping a group of objects means.
    /// </summary>
    private void Flip(bool horizontal)
    {
        var targets = _session.Selection.Where(o => !o.Locked).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        RectMm union = targets[0].Bounds;
        foreach (LabelObject o in targets.Skip(1))
        {
            union = union.Union(o.Bounds);
        }

        var commands = new List<IUndoableCommand>();
        foreach (LabelObject obj in targets)
        {
            if (obj is ImageObject image)
            {
                ImageAdjustments before = image.Adjustments.Clone();
                ImageAdjustments after = image.Adjustments.Clone();
                if (horizontal)
                {
                    after.FlipHorizontal = !after.FlipHorizontal;
                }
                else
                {
                    after.FlipVertical = !after.FlipVertical;
                }

                commands.Add(new SetPropertyCommand<ImageAdjustments>(
                    "Flip image", () => image.Adjustments, v => image.Adjustments = v, after, (image.Id, "flip")));
                _ = before;
            }

            if (targets.Count > 1)
            {
                RectMm b = obj.Bounds;
                RectMm mirrored = horizontal
                    ? b with { X = union.X + union.Width - (b.X - union.X) - b.Width }
                    : b with { Y = union.Y + union.Height - (b.Y - union.Y) - b.Height };
                commands.Add(new TransformObjectCommand(obj, mirrored, obj.RotationDegrees));
            }
        }

        if (commands.Count > 0)
        {
            _session.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand("Flip", commands));
            RefreshCanvas();
        }
    }

    private void RotateSelection(double degrees)
    {
        var commands = _session.Selection
            .Where(o => !o.Locked)
            .Select(obj => (IUndoableCommand)new TransformObjectCommand(
                obj, obj.Bounds, ((obj.RotationDegrees + degrees) % 360 + 360) % 360))
            .ToList();

        if (commands.Count > 0)
        {
            _session.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand("Rotate", commands));
            _properties.RefreshAll();
            RefreshCanvas();
        }
    }

    private void AddObject(LabelObject obj)
    {
        _session.Execute(new AddObjectCommand(_session.ActiveLabel, obj));
        _session.SetSelection(new[] { obj });
        RebuildObjectList();
    }

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp" };

    private void AddImage()
    {
        string? path = _shell.PickOpenFile("Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp|All files|*.*", "Add image");
        if (path is not null)
        {
            AddImageFromFile(path, atMm: null);
        }
    }

    /// <summary>Shared by the Insert&gt;Image dialog and drag-and-drop onto the canvas.</summary>
    private void AddImageFromFile(string path, Core.Units.PointMm? atMm)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            string id = _session.Document.Assets.Add(bytes, Path.GetExtension(path));
            (int w, int h) = ImageDimensions.Probe(bytes);
            double aspect = h > 0 ? (double)w / h : 1.0;
            double boxW = Math.Min(_session.ActiveLabel.SizeMm.Width - 4, 30);
            double boxH = aspect > 0 ? boxW / aspect : boxW;

            RectMm bounds = atMm is { } at
                ? new RectMm(
                    Math.Clamp(at.X - (boxW / 2), 0, Math.Max(0, _session.ActiveLabel.SizeMm.Width - boxW)),
                    Math.Clamp(at.Y - (boxH / 2), 0, Math.Max(0, _session.ActiveLabel.SizeMm.Height - boxH)),
                    boxW, boxH)
                : NewBounds(boxW, boxH);

            var obj = new ImageObject
            {
                AssetId = id,
                SourceFileName = Path.GetFileName(path),
                SourcePixelWidth = w,
                SourcePixelHeight = h,
                Bounds = bounds,
            };
            _imageSource.Invalidate();
            AddObject(obj);
        }
        catch (Exception ex)
        {
            _shell.Warn($"Could not load \"{Path.GetFileName(path)}\": {ex.Message}");
        }
    }

    private void ReplaceSelectedImage()
    {
        if (_session.Selection.Count != 1 || _session.Selection[0] is not ImageObject img)
        {
            return;
        }

        string? path = _shell.PickOpenFile("Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp|All files|*.*", "Replace image");
        if (path is null)
        {
            return;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            string id = _session.Document.Assets.Add(bytes, Path.GetExtension(path));
            (int w, int h) = ImageDimensions.Probe(bytes);
            string name = Path.GetFileName(path);

            var command = new CompositeCommand("Replace image", new IUndoableCommand[]
            {
                new SetPropertyCommand<string>("Replace image", () => img.AssetId, v => img.AssetId = v, id, (img.Id, "assetId")),
                new SetPropertyCommand<string?>("Replace image", () => img.SourceFileName, v => img.SourceFileName = v, name, (img.Id, "sourceFileName")),
                new SetPropertyCommand<int>("Replace image", () => img.SourcePixelWidth, v => img.SourcePixelWidth = v, w, (img.Id, "sourcePixelWidth")),
                new SetPropertyCommand<int>("Replace image", () => img.SourcePixelHeight, v => img.SourcePixelHeight = v, h, (img.Id, "sourcePixelHeight")),
            });
            _session.Execute(command);
            _imageSource.Invalidate();
            _properties.RefreshAll();
            RebuildObjectList();
            StatusText = $"Replaced image with {name}";
        }
        catch (Exception ex)
        {
            _shell.Warn($"Could not load \"{Path.GetFileName(path)}\": {ex.Message}");
        }
    }

    /// <summary>
    /// Toggles the lock on the list row that was clicked, or — when invoked from the
    /// Ctrl+L shortcut with no parameter — on everything currently selected.
    /// </summary>
    private void ToggleObjectLock(ObjectListItem? item)
    {
        var targets = item is not null
            ? new List<LabelObject> { item.Object }
            : _session.Selection.ToList();

        if (targets.Count == 0)
        {
            return;
        }

        bool locking = !targets[0].Locked;
        var commands = targets
            .Select(obj => (IUndoableCommand)new SetPropertyCommand<bool>(
                "Toggle lock", () => obj.Locked, v => obj.Locked = v, locking, (obj.Id, "locked")))
            .ToList();

        _session.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand("Toggle lock", commands));
        RebuildObjectList();
        _properties.RefreshAll();
    }

    private void OnImagesDroppedOnCanvas(IReadOnlyList<string> paths, Core.Units.PointMm atMm)
    {
        var images = paths.Where(p => ImageExtensions.Contains(Path.GetExtension(p).ToLowerInvariant())).ToList();
        if (images.Count == 0)
        {
            _shell.Warn("Only image files (PNG, JPG, BMP, GIF, TIFF, WEBP) can be dropped onto the canvas.");
            return;
        }

        for (int i = 0; i < images.Count; i++)
        {
            var at = new Core.Units.PointMm(atMm.X + (i * 4), atMm.Y + (i * 4));
            AddImageFromFile(images[i], at);
        }

        StatusText = images.Count == 1 ? $"Added {Path.GetFileName(images[0])}" : $"Added {images.Count} images";
    }

    private void AddDataField()
    {
        string field = _session.Data?.Columns.FirstOrDefault()?.Name ?? "Field";
        AddObject(new TextObject { Name = field, Content = RichTextContent.FromPlainText($"{{{field}}}"), Bounds = NewBounds(35, 7) });
    }

    private void DeleteSelection()
    {
        var locked = _session.Selection.Where(o => o.Locked).ToList();
        var deletable = _session.Selection.Where(o => !o.Locked).ToList();
        if (deletable.Count == 0)
        {
            if (locked.Count > 0)
            {
                StatusText = locked.Count == 1
                    ? "That object is locked — unlock it first to delete it."
                    : $"Those {locked.Count} objects are locked — unlock them first to delete them.";
            }

            return;
        }

        _session.Execute(new RemoveObjectsCommand(_session.ActiveLabel, deletable));
        _session.SetSelection(locked);
        RebuildObjectList();
        StatusText = locked.Count > 0
            ? $"Deleted {deletable.Count} object(s); skipped {locked.Count} locked."
            : $"Deleted {deletable.Count} object(s).";
    }

    private void DuplicateSelection()
    {
        var clones = _session.Selection.Select(o =>
        {
            LabelObject c = o.Clone();
            c.Bounds = c.Bounds.Offset(2, 2);
            return c;
        }).ToList();

        if (clones.Count == 0)
        {
            return;
        }

        _session.Execute(new CompositeCommand("Duplicate",
            clones.Select(c => (IUndoableCommand)new AddObjectCommand(_session.ActiveLabel, c)).ToList()));
        _session.SetSelection(clones);
        RebuildObjectList();
    }

    /// <summary>
    /// Ctrl+V drops a copy near the original; "Paste here" on the canvas menu passes
    /// true so it lands at the click point. The canvas remembers the last right-click
    /// forever, so plain Ctrl+V must not use it or the paste jumps somewhere stale.
    /// </summary>
    private void PasteObjects(bool atContextPoint = false)
    {
        var atMm = atContextPoint ? _canvas?.ContextMm : null;
        IReadOnlyList<LabelObject> pasted = atMm is { } p
            ? ObjectClipboard.PasteAt(new Core.Units.PointMm(p.X, p.Y))
            : ObjectClipboard.Paste();
        if (pasted.Count == 0)
        {
            return;
        }

        // keep pasted objects inside the label
        var size = _session.ActiveLabel.SizeMm;
        foreach (LabelObject o in pasted)
        {
            double x = Math.Clamp(o.Bounds.X, 0, Math.Max(0, size.Width - o.Bounds.Width));
            double y = Math.Clamp(o.Bounds.Y, 0, Math.Max(0, size.Height - o.Bounds.Height));
            o.Bounds = o.Bounds with { X = x, Y = y };
        }

        _session.Execute(new CompositeCommand("Paste",
            pasted.Select(c => (IUndoableCommand)new AddObjectCommand(_session.ActiveLabel, c)).ToList()));
        _session.SetSelection(pasted);
        RebuildObjectList();
    }

    /// <summary>
    /// Renames whichever page is selected — including the master label, which used to
    /// refuse and so looked like "renaming pages doesn't work" on a project that has
    /// no standalone pages yet.
    /// </summary>
    private void RenameActivePage()
    {
        string current = ActivePageName;
        string? name = _shell.Prompt("Rename page", "Page name:", current);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == current)
        {
            return;
        }

        _session.RenameActivePage(name.Trim());
        OnPropertyChanged(nameof(ActivePageName));
        OnPropertyChanged(nameof(DocumentTabTitle));
    }

    private void GroupSelection()
    {
        string groupId = Guid.NewGuid().ToString("N");
        var commands = _session.Selection
            .Select(o => (IUndoableCommand)new SetPropertyCommand<string?>("Group", () => o.GroupId, v => o.GroupId = v, groupId))
            .ToList();
        _session.Execute(new CompositeCommand("Group", commands));
    }

    private void UngroupSelection()
    {
        var commands = _session.Selection
            .Where(o => o.GroupId is not null)
            .Select(o => (IUndoableCommand)new SetPropertyCommand<string?>("Ungroup", () => o.GroupId, v => o.GroupId = v, null))
            .ToList();
        if (commands.Count > 0)
        {
            _session.Execute(new CompositeCommand("Ungroup", commands));
        }
    }

    private void Reorder(int delta)
    {
        var objects = _session.ActiveLabel.Objects;
        var commands = new List<IUndoableCommand>();
        foreach (LabelObject obj in _session.Selection.OrderBy(o => objects.IndexOf(o)))
        {
            int current = objects.IndexOf(obj);
            int target = delta switch
            {
                int.MaxValue => objects.Count - 1,
                int.MinValue => 0,
                _ => Math.Clamp(current + delta, 0, objects.Count - 1),
            };
            if (target != current)
            {
                commands.Add(new ReorderObjectCommand(_session.ActiveLabel, obj, target));
            }
        }

        if (commands.Count > 0)
        {
            _session.Execute(new CompositeCommand("Reorder", commands));
            RebuildObjectList();
        }
    }

    private sealed class AlignAccessor
    {
        public double X, Y, W, H;
    }

    private void Align(Action<AlignAccessor> mutate)
    {
        var commands = new List<IUndoableCommand>();
        foreach (LabelObject obj in _session.Selection)
        {
            var a = new AlignAccessor { X = obj.Bounds.X, Y = obj.Bounds.Y, W = obj.Bounds.Width, H = obj.Bounds.Height };
            mutate(a);
            RectMm target = new(a.X, a.Y, a.W, a.H);
            if (target != obj.Bounds)
            {
                commands.Add(new TransformObjectCommand(obj, target, obj.RotationDegrees));
            }
        }

        if (commands.Count > 0)
        {
            _session.Execute(new CompositeCommand("Align", commands));
        }
    }

    // ---------- file ----------

    private void NewProject()
    {
        if (!ConfirmDiscard())
        {
            return;
        }

        _shell.ShowNewProject(template =>
        {
            _env.Settings.LastTemplateId = template.Id;
            ProjectModel project = ProjectModel.CreateNew(template, "Untitled");
            SeedStarterLabel(project);
            LoadSession(new ProjectDocument { Project = project });
            StatusText = $"New project · {template.Name}";
        });
    }

    private void OpenProject()
    {
        if (!ConfirmDiscard())
        {
            return;
        }

        string? path = _shell.PickOpenFile("Label Maker project|*.labelproj|All files|*.*", "Open project");
        if (path is not null)
        {
            OpenProjectPath(path);
        }
    }

    public void OpenProjectPath(string path)
    {
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
            path = _shell.PickSaveFile("Label Maker project|*.labelproj", SafeName(_session.Project.Metadata.Title), "Save project");
            if (path is null)
            {
                return false;
            }
        }

        try
        {
            ProjectFile.Save(_session.Document, path);
            _env.Settings.AddRecent(path, _session.Project.Metadata.Title);
            _env.Settings.LastTemplateId = _session.Project.Template?.Id;
            _env.SaveSettings();
            _env.AutoSave.ClearFor(_session.Project.Metadata.Id);
            RefreshRecent();
            OnPropertyChanged(nameof(Title));
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

        bool? choice = _shell.ConfirmSaveChanges(_session.Project.Metadata.Title);
        return choice switch
        {
            true => Save(false),
            false => true,
            _ => false,
        };
    }

    // ---------- data ----------

    private void ImportData()
    {
        string? path = _shell.PickOpenFile("Data files|*.csv;*.tsv;*.txt;*.xlsx|CSV|*.csv|Excel|*.xlsx|All files|*.*", "Import data");
        if (path is null)
        {
            return;
        }

        try
        {
            var service = new DataImportService();
            var connection = new DataConnectionModel
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Kind = path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? DataSourceKind.Xlsx : DataSourceKind.Csv,
                SourcePath = path,
            };
            RawTable raw = service.ReadRaw(path, new TabularReadOptions());
            DataSet data = service.BuildDataSet(raw, connection);

            // cache a CSV snapshot inside the project
            _session.Document.DataCaches.RemoveAll(c => c.ConnectionId == connection.Id);
            _session.Document.DataCaches.Add(new ProjectDataCache(connection.Id, ToCsv(data), "{}"));

            _session.SetData(data, connection);
            StatusText = $"Imported {data.RowCount} records, {data.Columns.Count} columns from {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            _shell.Warn("Import failed:\n\n" + ex.Message);
        }
    }

    private void DuplicateCheck()
    {
        if (_session.Data is not { RowCount: > 0 } data)
        {
            _shell.Info("Import a data source first.");
            return;
        }

        string col = data.Columns.FirstOrDefault(c => c.Name.Contains("barcode", StringComparison.OrdinalIgnoreCase))?.Name
                     ?? data.Columns.FirstOrDefault(c => c.Name.Contains("serial", StringComparison.OrdinalIgnoreCase))?.Name
                     ?? data.Columns.First().Name;

        DuplicateReport report = DuplicateDetector.CheckColumn(data, col, DuplicatePolicy.Warn);
        string body = report.HasDuplicates
            ? $"Column: {col}\n\n" + string.Join('\n', report.Describe(col))
            : $"No duplicate values found in column '{col}'.";
        _shell.ShowTextReport("Duplicate check", body);
    }

    private void RunPreflight()
    {
        var env = new PreflightEnvironment
        {
            AssetExists = id => _session.Document.Assets.Contains(id),
            ImagePixelSize = id =>
            {
                byte[]? b = _session.Document.Assets.GetBytes(id);
                if (b is null)
                {
                    return null;
                }

                (int w, int h) = ImageDimensions.Probe(b);
                return w > 0 ? (w, h) : null;
            },
            FontInstalled = FontIsInstalled,
        };

        PreflightReport report = new PreflightChecker(env).Check(_session.Project, _session.Data);
        string body = report.IsClean
            ? "No problems found."
            : string.Join('\n', report.Issues.OrderBy(i => i.Severity)
                .Select(i => $"[{(i.Severity == PreflightSeverity.Error ? "ERROR" : "warn")}] {i.Category}: {i.Message}"))
              + $"\n\n{report.Errors.Count()} error(s), {report.Warnings.Count()} warning(s).";
        _shell.ShowTextReport("Preflight check", body);
    }

    private static bool FontIsInstalled(string family)
    {
        try
        {
            return System.Windows.Media.Fonts.SystemFontFamilies
                .Any(f => f.Source.Equals(family, StringComparison.OrdinalIgnoreCase) ||
                          f.FamilyNames.Values.Any(n => n.Equals(family, StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception)
        {
            return true;
        }
    }

    // ---------- print / export ----------

    private void Print()
    {
        try
        {
            var settings = _session.Project.PrintSettings;
            if (!_hasConfiguredPrintSettings)
            {
                settings.Content = _session.Data is { RowCount: > 0 } ? PrintContent.VariableData : PrintContent.SheetFill;
            }

            // The app's own print dialog, not the Windows one; it still offers the
            // driver dialog for duplex/tray options the printer alone knows about.
            if (_shell.ShowPrintDialog())
            {
                StatusText = "Sent to printer";
                _env.SaveSettings();
            }
        }
        catch (Exception ex)
        {
            _shell.Warn("Printing failed:\n\n" + ex.Message);
        }
    }

    private void OpenPrintSettingsThenPrint()
    {
        if (_shell.ShowPrintSettings())
        {
            _hasConfiguredPrintSettings = true;
            Print();
        }
    }

    private void ExportSheetPdf()
    {
        string? path = _shell.PickSaveFile("PDF|*.pdf", SafeName(_session.Project.Metadata.Title) + "-sheet", "Export sheet PDF");
        if (path is null)
        {
            return;
        }

        try
        {
            var request = new ExportRequest
            {
                Project = _session.Project,
                Data = _session.Data,
                Scope = ExportScope.Sheet,
                Title = _session.Project.Metadata.Title,
            };
            File.WriteAllBytes(path, new PdfExporter().Export(request, _session.Document.Assets));
            StatusText = $"Exported {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            _shell.Warn("PDF export failed:\n\n" + ex.Message);
        }
    }

    private void ExportLabelPng()
    {
        string? path = _shell.PickSaveFile("PNG image|*.png", SafeName(_session.Project.Metadata.Title) + "-label", "Export label PNG");
        if (path is null || _session.Project.Template is not { } template)
        {
            return;
        }

        try
        {
            byte[] png = _imageExporter.RenderLabelToImage(
                _session.ActiveLabel, _session.CurrentResolveContext(), template, 300, RasterFormat.Png);
            File.WriteAllBytes(path, png);
            StatusText = $"Exported {Path.GetFileName(path)} (300 DPI)";
        }
        catch (Exception ex)
        {
            _shell.Warn("PNG export failed:\n\n" + ex.Message);
        }
    }

    private void ExportLabelSvg()
    {
        string? path = _shell.PickSaveFile("SVG|*.svg", SafeName(_session.Project.Metadata.Title) + "-label", "Export label SVG");
        if (path is null)
        {
            return;
        }

        try
        {
            LabelShape shape = _session.Project.Template?.Grid.LabelShape ?? LabelShape.Rectangle;
            double radius = _session.Project.Template?.Grid.CornerRadiusMm ?? 0;
            string svg = new SvgExporter().ExportLabel(
                _session.ActiveLabel, _session.CurrentResolveContext(), _session.Document.Assets, shape, radius);
            File.WriteAllText(path, svg);
            StatusText = $"Exported {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            _shell.Warn("SVG export failed:\n\n" + ex.Message);
        }
    }

    // ---------- search / lists ----------

    private void RefreshSearch()
    {
        _session.RebuildSearchIndex();
        SearchResults.Clear();
        if (string.IsNullOrWhiteSpace(_searchText))
        {
            return;
        }

        foreach (var result in _session.SearchIndex.Search(_searchText, 60))
        {
            SearchResults.Add(new SearchListItem(result));
        }
    }

    private void NavigateToSearchResult(SearchListItem item)
    {
        var r = item.Result;
        if (r.RecordIndex is { } record)
        {
            _session.RecordIndex = record;
        }

        if (r.ObjectId is { } objectId)
        {
            LabelObject? obj = _session.ActiveLabel.FindObject(objectId);
            if (obj is not null)
            {
                _session.SetSelection(new[] { obj });
            }
        }

        StatusText = $"Jumped to {r.Title}";
    }

    private void RebuildObjectList()
    {
        ObjectList.Clear();
        for (int i = _session.ActiveLabel.Objects.Count - 1; i >= 0; i--)
        {
            ObjectList.Add(new ObjectListItem(_session.ActiveLabel.Objects[i]));
        }
    }

    private void RefreshRecent()
    {
        _env.Settings.PruneMissingRecents();
        RecentProjects.Clear();
        foreach (var r in _env.Settings.RecentProjects)
        {
            RecentProjects.Add(new RecentEntry(r.Path, r.Title));
        }
    }

    private void RefreshCanvas() => _canvas?.InvalidateVisual();

    private void OnDocumentChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SelectionInfo));
        OnPropertyChanged(nameof(DocumentTabTitle));
        OnPropertyChanged(nameof(SaveStateText));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(SheetSummary));
        OnPropertyChanged(nameof(TemplateSummary));
        RebuildObjectList();
        RefreshCanvas();
        RaiseAllCanExecute();
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectionInfo));
        _properties.RefreshAll();
        RefreshCanvas();
        RaiseAllCanExecute();
    }

    private void RaiseAllCanExecute()
    {
        foreach (var cmd in new[]
                 {
                     UndoCommand, RedoCommand, DeleteCommand, DuplicateCommand, GroupCommand, UngroupCommand,
                     CopyCommand, CutCommand, PasteCommand,
                     BringToFrontCommand, BringForwardCommand, SendBackwardCommand, SendToBackCommand,
                     AlignLeftCommand, AlignTopCommand, CenterHorizontalCommand, CenterVerticalCommand, ClearDataCommand,
                     RemovePageCommand, RenamePageCommand, ReplaceImageCommand,
                 })
        {
            cmd.RaiseCanExecuteChanged();
        }
    }

    private static string SafeName(string title)
    {
        Span<char> buffer = stackalloc char[title.Length == 0 ? 1 : title.Length];
        int n = 0;
        foreach (char c in title)
        {
            buffer[n++] = Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c;
        }

        return n == 0 ? "label" : new string(buffer[..n]);
    }

    private static string ToCsv(DataSet data)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join(',', data.Columns.Select(c => Escape(c.Name))));
        foreach (var row in data.Rows)
        {
            sb.AppendLine(string.Join(',', data.Columns.Select(c => Escape(row[c.Name]))));
        }

        return sb.ToString();

        static string Escape(string v) =>
            v.Contains(',') || v.Contains('"') || v.Contains('\n') ? '"' + v.Replace("\"", "\"\"") + '"' : v;
    }
}

public sealed record RecentEntry(string Path, string Title);

