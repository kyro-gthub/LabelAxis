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
    public RelayCommand PrintHistoryCommand { get; private set; } = null!;
    public RelayCommand WelcomeCommand { get; private set; } = null!;
    public RelayCommand NewThermalLabelCommand { get; private set; } = null!;
    public RelayCommand ThermalPrintCommand { get; private set; } = null!;
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
    public RelayCommand AddTableCommand { get; private set; } = null!;
    public RelayCommand DataTableCommand { get; private set; } = null!;
    public RelayCommand LabelSizeCommand { get; private set; } = null!;
    public RelayCommand SelectAllCommand { get; private set; } = null!;
    public RelayCommand CropImageCommand { get; private set; } = null!;
    public RelayCommand ClearCropCommand { get; private set; } = null!;
    public RelayCommand PickColorCommand { get; private set; } = null!;
    public RelayCommand ApplyTextStyleCommand { get; private set; } = null!;
    public RelayCommand DrawCustomShapeCommand { get; private set; } = null!;
    public RelayCommand ApplyLayoutCommand { get; private set; } = null!;
    public RelayCommand HelpCommand { get; private set; } = null!;
    public RelayCommand EditShapeDotsCommand { get; private set; } = null!;
    public RelayCommand ApplySwatchCommand { get; private set; } = null!;
    public RelayCommand AlignRightCommand { get; private set; } = null!;
    public RelayCommand AlignBottomCommand { get; private set; } = null!;
    public RelayCommand DistributeHorizontallyCommand { get; private set; } = null!;
    public RelayCommand DistributeVerticallyCommand { get; private set; } = null!;
    public RelayCommand MatchWidthCommand { get; private set; } = null!;
    public RelayCommand MatchHeightCommand { get; private set; } = null!;
    public RelayCommand MatchSizeCommand { get; private set; } = null!;
    public RelayCommand FreeTransformCommand { get; private set; } = null!;
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
        CopyCommand = new RelayCommand(() => ObjectClipboard.Set(_session.Selection, _session.Document.Assets), () => _session.Selection.Count > 0);
        CutCommand = new RelayCommand(() => { ObjectClipboard.Set(_session.Selection, _session.Document.Assets); DeleteSelection(); }, () => _session.Selection.Count > 0);
        PasteCommand = new RelayCommand(() => PasteObjects(), () => ObjectClipboard.HasContent);
        PasteHereCommand = new RelayCommand(() => PasteObjects(atContextPoint: true), () => ObjectClipboard.HasContent);
        GroupCommand = new RelayCommand(GroupSelection, () => _session.Selection.Count > 1);
        UngroupCommand = new RelayCommand(UngroupSelection, () => _session.Selection.Any(o => o.GroupId is not null));

        AddPageCommand = new RelayCommand(() => _session.AddPage());
        DuplicatePageCommand = new RelayCommand(() => _session.DuplicateActivePage());
        RemovePageCommand = new RelayCommand(
            RemoveActivePage,
            () => !_session.IsEditingMaster || _session.Project.Pages.Count > 0);
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
        AddTableCommand = new RelayCommand(() =>
        {
            var table = new TableObject
            {
                Name = "Table",
                Rows = 3,
                Columns = 2,
                Cells = new List<string> { "Item", "Value", "Size", "{Size}", "Batch", "{Batch}" },
                ColumnWeights = new List<double> { 1, 1 },
                RowWeights = new List<double> { 1, 1, 1 },
                FirstRowIsHeader = true,
                HeaderFill = new ColorRgba(0xE8, 0xF1, 0xFE),
                Bounds = NewBounds(50, 18),
            };
            AddObject(table);
        });
        AddDataFieldCommand = new RelayCommand(AddDataField);
        AddDateFieldCommand = new RelayCommand(() => AddObject(new TextObject { Name = "Date", Content = RichTextContent.FromPlainText("{CurrentDate}"), Bounds = NewBounds(30, 6) }));

        BringToFrontCommand = new RelayCommand(() => Reorder(int.MaxValue), () => _session.Selection.Count > 0);
        BringForwardCommand = new RelayCommand(() => Reorder(+1), () => _session.Selection.Count > 0);
        SendBackwardCommand = new RelayCommand(() => Reorder(-1), () => _session.Selection.Count > 0);
        SendToBackCommand = new RelayCommand(() => Reorder(int.MinValue), () => _session.Selection.Count > 0);

        AlignLeftCommand = new RelayCommand(() => AlignSelection(AlignEdge.Left), AnySel);
        AlignTopCommand = new RelayCommand(() => AlignSelection(AlignEdge.Top), AnySel);
        AlignRightCommand = new RelayCommand(() => AlignSelection(AlignEdge.Right), AnySel);
        AlignBottomCommand = new RelayCommand(() => AlignSelection(AlignEdge.Bottom), AnySel);
        CenterHorizontalCommand = new RelayCommand(() => AlignSelection(AlignEdge.CentreX), AnySel);
        CenterVerticalCommand = new RelayCommand(() => AlignSelection(AlignEdge.CentreY), AnySel);
        DistributeHorizontallyCommand = new RelayCommand(() => DistributeSelection(horizontal: true), () => _session.Selection.Count >= 3);
        DistributeVerticallyCommand = new RelayCommand(() => DistributeSelection(horizontal: false), () => _session.Selection.Count >= 3);
        MatchWidthCommand = new RelayCommand(() => MatchSize(width: true, height: false), MultiSel);
        MatchHeightCommand = new RelayCommand(() => MatchSize(width: false, height: true), MultiSel);
        MatchSizeCommand = new RelayCommand(() => MatchSize(width: true, height: true), MultiSel);
        FreeTransformCommand = new RelayCommand(() =>
        {
            if (_canvas is not null)
            {
                _canvas.FreeTransform = !_canvas.FreeTransform;
                StatusText = _canvas.FreeTransform
                    ? "Free transform: corners resize, side handles skew, the knob rotates. Press it again to finish."
                    : "Free transform off";
            }
        });

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
        PrintPreviewCommand = new RelayCommand(() =>
        {
            AimPrintAtActivePage();
            _shell.ShowPrintPreview();
        });
        PrintHistoryCommand = new RelayCommand(ShowPrintHistory);
        WelcomeCommand = new RelayCommand(ShowWelcome);
        NewThermalLabelCommand = new RelayCommand(NewThermalLabel);
        ThermalPrintCommand = new RelayCommand(() =>
        {
            if (_shell.ShowThermalPrint())
            {
                StatusText = "Sent to the label printer";
                _env.SaveSettings();
            }
        });
        BuildDataCommands();
        BuildTabCommands();
        BuildPublisherCommands();
        BuildPanelCommands();
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
        DrawCustomShapeCommand = new RelayCommand(() => _canvas?.BeginCustomShape());
        ApplyLayoutCommand = new RelayCommand(ApplyLayoutToOtherPages, () => _session.ActiveLabel.Objects.Count > 0);
        HelpCommand = new RelayCommand(p => HelpGuide.Show(System.Windows.Application.Current?.MainWindow,
            p as string is { Length: > 0 } topic ? topic : HelpTopicForSelection()));
        EditShapeDotsCommand = new RelayCommand(
            () => { if (_session.Selection is [ShapeObject { Shape: ShapeKind.Custom } s]) { _canvas?.BeginPointEdit(s); } },
            () => _session.Selection is [ShapeObject { Shape: ShapeKind.Custom, Locked: false }]);
        ApplyTextStyleCommand = new RelayCommand(ApplyTextStyleToOtherPages,
            () => _session.Selection.OfType<TextObject>().Any());
        PickColorCommand = new RelayCommand(p =>
        {
            string target = p as string ?? "fill";
            string currentHex = target switch
            {
                "stroke" => _properties.StrokeHex,
                "gradient" => _properties.GradientToHex,
                "text" => _properties.TextColorHex,
                "highlight" => _properties.HighlightHex,
                "bar" => _properties.BarColorHex,
                "barbg" => _properties.BarBackgroundHex,
                "tablefill" => _properties.TableHeaderFillHex,
                "tableborder" => _properties.TableBorderHex,
                "imageborder" => _properties.ImageBorderHex,
                _ => _properties.FillHex,
            };

            if (!ColorRgba.TryParse(currentHex, out ColorRgba current))
            {
                current = target is "stroke" or "text" or "imageborder" ? ColorRgba.Black : ColorRgba.Transparent;
            }

            string title = target switch
            {
                "stroke" => "Stroke colour",
                "gradient" => "Gradient end colour",
                "text" => "Text colour",
                "highlight" => "Highlight colour",
                "bar" => "Bar colour",
                "barbg" => "Barcode background",
                "tablefill" => "Header fill",
                "tableborder" => "Table border",
                "imageborder" => "Image border colour",
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
            "LabelAxis 1.0\n\nWindows-first label design & printing.\n" +
            "Label sheet templates · barcodes · variable data · accurate printing.\n\n" +
            (_env.IsPortable ? "Portable build — settings and templates live in " : "Settings and templates live in ") +
            _env.Paths.RoamingDirectory,
            "About LabelAxis"));
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

            if (obj is ShapeObject { Shape: ShapeKind.Custom } custom)
            {
                List<PointMm> mirrored = CustomShape.Mirrored(custom.CustomPoints, horizontal);
                commands.Add(new SetPropertyCommand<List<PointMm>>(
                    "Flip shape", () => custom.CustomPoints, v => custom.CustomPoints = v, mirrored, (custom.Id, "flip")));
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
            AddImageFromBytes(File.ReadAllBytes(path), Path.GetExtension(path), Path.GetFileName(path), atMm);
        }
        catch (Exception ex)
        {
            _shell.Warn($"Could not load \"{Path.GetFileName(path)}\": {ex.Message}");
        }
    }

    /// <summary>Shared by files, drag-and-drop and bitmaps pasted from other apps.</summary>
    private void AddImageFromBytes(byte[] bytes, string extension, string? name, Core.Units.PointMm? atMm)
    {
        string id = _session.Document.Assets.Add(bytes, extension);
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
            SourceFileName = name,
            SourcePixelWidth = w,
            SourcePixelHeight = h,
            Bounds = bounds,
        };
        _imageSource.Invalidate();
        AddObject(obj);
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
            ? item.Members.ToList()
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
        // Copies step off the originals; a duplicated group becomes a new group of its own
        // instead of silently joining the original's.
        var clones = PastePlacement.Place(_session.Selection, _session.ActiveLabel).ToList();

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
        Core.Units.PointMm? atMm = atContextPoint ? _canvas?.ContextMm : null;

        switch (ObjectClipboard.Read())
        {
            case ClipboardContent.Objects objects:
                PasteLabelObjects(objects, atMm);
                break;

            case ClipboardContent.Image image:
                AddImageFromBytes(image.Png, ".png", "Pasted image", atMm);
                StatusText = "Pasted an image from the clipboard";
                break;

            case ClipboardContent.Files files:
                foreach (string path in files.Paths)
                {
                    AddImageFromFile(path, atMm);
                }

                break;

            case ClipboardContent.Text text:
                PasteText(text.Value, atMm);
                break;
        }
    }

    /// <summary>Text from another app becomes a text box sized for a couple of lines.</summary>
    private void PasteText(string value, Core.Units.PointMm? atMm)
    {
        string text = value.Replace("\r\n", "\n").TrimEnd('\n');
        int lines = Math.Clamp(text.Split('\n').Length, 1, 12);
        RectMm bounds = NewBounds(60, 5.5 * lines);
        if (atMm is { } at)
        {
            bounds = bounds with { X = at.X, Y = at.Y };
        }

        AddObject(new TextObject { Content = RichTextContent.FromPlainText(text), Bounds = bounds });
        StatusText = "Pasted text from the clipboard";
    }

    private void PasteLabelObjects(ClipboardContent.Objects clip, Core.Units.PointMm? atMm)
    {
        // The copies keep their layout and groups as one block (see PastePlacement).
        IReadOnlyList<LabelObject> pasted = PastePlacement.Place(clip.Items, _session.ActiveLabel, atMm);
        if (pasted.Count == 0)
        {
            return;
        }

        // Images copied from another project bring their pixels along.
        foreach (ImageObject image in pasted.OfType<ImageObject>())
        {
            if (!_session.Document.Assets.Contains(image.AssetId) &&
                clip.Assets.TryGetValue(image.AssetId, out (string Extension, byte[] Bytes) asset))
            {
                image.AssetId = _session.Document.Assets.Add(asset.Bytes, asset.Extension);
            }
        }

        _imageSource.Invalidate();


        _session.Execute(new CompositeCommand("Paste",
            pasted.Select(c => (IUndoableCommand)new AddObjectCommand(_session.ActiveLabel, c)).ToList()));
        _session.SetSelection(pasted);
        RebuildObjectList();
    }

    /// <summary>
    /// Removes the selected page. Removing the master label (the first row) makes the next
    /// page the master label; the last page left cannot be removed.
    /// </summary>
    private void RemoveActivePage()
    {
        if (_session.IsEditingMaster && _session.Project.Pages.Count == 0)
        {
            StatusText = "A project needs at least one page, so the last page cannot be removed.";
            return;
        }

        string question = _session.IsEditingMaster
            ? $"Remove page \"{ActivePageName}\"? \"{_session.Project.Pages[0].Name}\" becomes the first page (the master label)."
            : $"Remove page \"{ActivePageName}\"?";
        if (!_env.Settings.ConfirmDestructiveActions || _shell.Confirm(question))
        {
            _session.RemoveActivePage();
            StatusText = "Page removed. Undo (Ctrl+Z) brings it back.";
        }
    }

    /// <summary>
    /// Renames whichever page is selected — including the master label, which used to
    /// refuse and so looked like "renaming pages doesn't work" on a project that has
    /// no standalone pages yet.
    /// </summary>
    private void RenameActivePage()
    {
        // The page list shows a text box on the page's own row instead of a pop-up.
        SelectedLeftTab = 1;
        PageRenameRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Asks the view to start renaming the selected page in the page list.</summary>
    public event EventHandler? PageRenameRequested;

    /// <summary>Applies the name typed on the page's row; blank or unchanged names are ignored.</summary>
    public void CommitPageRename(string? name)
    {
        string current = ActivePageName;
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == current)
        {
            return;
        }

        _session.RenameActivePage(name.Trim());
        StatusText = $"Page renamed to \"{name.Trim()}\"";
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
        // One command for the whole selection: one command per object, each remembering its
        // index from before the others moved, jumbled the order when several were selected.
        var command = new ArrangeObjectsCommand(_session.ActiveLabel,
            ObjectStacking.Step(_session.ActiveLabel.Objects, _session.Selection.ToList(), delta));
        if (command.ChangesAnything)
        {
            _session.Execute(command);
            RebuildObjectList();
        }
    }

    private enum AlignEdge
    {
        Left,
        CentreX,
        Right,
        Top,
        CentreY,
        Bottom,
    }

    private bool _alignToLabel;

    /// <summary>
    /// When set, alignment uses the label's edges even with several objects selected.
    /// Otherwise one object aligns to the label and several align to each other.
    /// </summary>
    public bool AlignToLabel
    {
        get => _alignToLabel;
        set => SetField(ref _alignToLabel, value);
    }

    /// <summary>
    /// Aligns by what you see: the edges of each object's rotated/skewed outline,
    /// not its un-rotated box, so a tilted barcode lines up with its neighbours.
    /// </summary>
    private void AlignSelection(AlignEdge edge)
    {
        var targets = _session.Selection.Where(o => !o.Locked).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        RectMm reference = targets.Count == 1 || _alignToLabel
            ? _session.ActiveLabel.Bounds
            : _session.Selection.Select(o => o.RotatedBounds).Aggregate((a, b) => a.Union(b));

        var commands = new List<IUndoableCommand>();
        foreach (LabelObject obj in targets)
        {
            RectMm visual = obj.RotatedBounds;
            double dx = edge switch
            {
                AlignEdge.Left => reference.Left - visual.Left,
                AlignEdge.CentreX => reference.Center.X - visual.Center.X,
                AlignEdge.Right => reference.Right - visual.Right,
                _ => 0,
            };
            double dy = edge switch
            {
                AlignEdge.Top => reference.Top - visual.Top,
                AlignEdge.CentreY => reference.Center.Y - visual.Center.Y,
                AlignEdge.Bottom => reference.Bottom - visual.Bottom,
                _ => 0,
            };

            if (Math.Abs(dx) > 1e-6 || Math.Abs(dy) > 1e-6)
            {
                commands.Add(new TransformObjectCommand(obj, obj.Bounds.Offset(dx, dy), obj.RotationDegrees));
            }
        }

        if (commands.Count > 0)
        {
            _session.Execute(new CompositeCommand("Align", commands));
        }
    }

    /// <summary>
    /// Equal gaps between objects: the outermost two stay put and everything in
    /// between is spaced so the space between neighbours is the same.
    /// </summary>
    private void DistributeSelection(bool horizontal)
    {
        var targets = _session.Selection.Where(o => !o.Locked).ToList();
        if (targets.Count < 3)
        {
            return;
        }

        var ordered = targets
            .OrderBy(o => horizontal ? o.RotatedBounds.Center.X : o.RotatedBounds.Center.Y)
            .ToList();

        RectMm first = ordered[0].RotatedBounds;
        RectMm last = ordered[^1].RotatedBounds;
        double span = horizontal ? last.Right - first.Left : last.Bottom - first.Top;
        double sizes = ordered.Sum(o => horizontal ? o.RotatedBounds.Width : o.RotatedBounds.Height);
        double gap = (span - sizes) / (ordered.Count - 1);

        var commands = new List<IUndoableCommand>();
        double cursor = horizontal ? first.Left : first.Top;
        foreach (LabelObject obj in ordered)
        {
            RectMm visual = obj.RotatedBounds;
            double delta = cursor - (horizontal ? visual.Left : visual.Top);
            if (Math.Abs(delta) > 1e-6)
            {
                RectMm moved = horizontal ? obj.Bounds.Offset(delta, 0) : obj.Bounds.Offset(0, delta);
                commands.Add(new TransformObjectCommand(obj, moved, obj.RotationDegrees));
            }

            cursor += (horizontal ? visual.Width : visual.Height) + gap;
        }

        if (commands.Count > 0)
        {
            _session.Execute(new CompositeCommand(horizontal ? "Distribute horizontally" : "Distribute vertically", commands));
        }
    }

    /// <summary>Gives every selected object the width and/or height of the first one selected.</summary>
    private void MatchSize(bool width, bool height)
    {
        if (_session.Selection.Count < 2)
        {
            return;
        }

        RectMm model = _session.Selection[0].Bounds;
        var commands = _session.Selection.Skip(1)
            .Where(o => !o.Locked)
            .Select(o =>
            {
                RectMm b = o.Bounds;
                var size = new SizeMm(width ? model.Width : b.Width, height ? model.Height : b.Height);
                return (IUndoableCommand)new TransformObjectCommand(o, RectMm.FromCenter(b.Center, size), o.RotationDegrees);
            })
            .ToList();

        if (commands.Count > 0)
        {
            _session.Execute(new CompositeCommand("Match size", commands));
        }
    }

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


            connection.LastRefreshedUtc = DateTimeOffset.UtcNow;
            UseRecords(data, connection);
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
            : string.Join("\n\n", report.SummaryLines())
              + $"\n\n{report.Errors.Count()} error(s), {report.Warnings.Count()} warning(s) in total.";
        _shell.ShowTextReport("Preflight check", body);
    }

    private static bool FontIsInstalled(string family)
    {
        if (BundledFonts.Contains(family))
        {
            return true;    // comes with LabelAxis
        }

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
            if (!_activeTab.HasConfiguredPrintSettings)
            {
                settings.Content = _session.Data is { RowCount: > 0 } ? PrintContent.VariableData : PrintContent.SheetFill;
            }

            AimPrintAtActivePage();

            // A roll goes to the thermal dialog (printer commands, dot-exact preview);
            // it can still hand over to the Windows driver from there.
            if (_session.Project.Template is { IsRoll: true })
            {
                if (_shell.ShowThermalPrint())
                {
                    StatusText = "Sent to the label printer";
                    _env.SaveSettings();
                }

                return;
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

    /// <summary>
    /// Printing from a numbered page prints that page: the Print dialog and the preview open
    /// on it, and the dialog can still switch to every page or a chosen few. From the master
    /// label, printing goes back to what the job was set to before.
    /// </summary>
    /// <summary>The help page about what is selected (F1 in the main window), or the first steps.</summary>
    public string HelpTopicForSelection() => _session.Selection.FirstOrDefault() switch
    {
        TextObject t when Bidi.HasRightToLeft(t.Content.PlainText) => "arabic",
        TextObject => "text",
        BarcodeObject => "barcodes",
        ImageObject => "images",
        ShapeObject => "shapes",
        TableObject => "designing",
        _ => HelpGuide.StartTopic,
    };

    /// <summary>Puts objects on other pages where they are on this page.</summary>
    private void ApplyLayoutToOtherPages()
    {
        if (_session.Project.Pages.Count == 0)
        {
            _shell.Info("This project has only one page. Add or duplicate a page first, then its objects can be lined up with this one.");
            return;
        }

        if (_shell.ShowApplyLayout())
        {
            RefreshCanvas();
            StatusText = "Positions applied to other pages";
        }
    }

    /// <summary>Gives the selected text box's formatting to text boxes on other pages.</summary>
    private void ApplyTextStyleToOtherPages()
    {
        if (_session.Selection.OfType<TextObject>().FirstOrDefault() is not { } source)
        {
            return;
        }

        if (_session.Project.Pages.Count == 0)
        {
            _shell.Info("This project has only one page. Add or duplicate a page first, then its text boxes can take this formatting.");
            return;
        }

        if (_shell.ShowApplyTextStyle(source))
        {
            RefreshCanvas();
            StatusText = "Formatting applied to other pages";
        }
    }

    private void AimPrintAtActivePage()
    {
        PrintSettings settings = _session.Project.PrintSettings;
        int position = _session.IsEditingMaster
            ? 0
            : _session.Project.Pages.FindIndex(p => p.Id == _session.ActivePageId) + 1;
        settings.CurrentPage = Math.Max(0, position);

        if (position > 0)
        {
            settings.Content = PrintContent.Pages;
            settings.PagesToPrint = PagesToPrint.Current;
        }
        else if (settings.PagesToPrint == PagesToPrint.Current)
        {
            settings.PagesToPrint = PagesToPrint.All;
        }
    }

    private void ShowPrintHistory()
    {
        if (_shell.ShowPrintHistory() is { } reprint)
        {
            Reprint(reprint);
        }
    }

    /// <summary>
    /// Sends a job from the print history again: opens its project if another one is
    /// showing, restores the settings it printed with (and the sheet range to finish
    /// a stopped job), then shows the print dialog so the user confirms before paper moves.
    /// </summary>
    public bool Reprint(ReprintRequest request)
    {
        PrintHistoryEntry entry = request.Entry;

        if (TabForProject(entry.ProjectId) is { } openTab)
        {
            ActivateTab(openTab);
        }
        else
        {
            if (entry.ProjectPath is not { } path || !File.Exists(path))
            {
                _shell.Warn($"The project \"{entry.ProjectTitle}\" is not open and its file could not be found, so the job cannot be reprinted.");
                return false;
            }

            if (!_shell.Confirm($"This job was printed from \"{entry.ProjectTitle}\". Open that project in a new tab to reprint it?\n\n{path}"))
            {
                return false;
            }

            OpenProjectPath(path);
            if (_session.Project.Metadata.Id != entry.ProjectId)
            {
                return false;
            }
        }

        if (entry.Content == PrintContent.VariableData && entry.DataRecordCount != _session.RecordCount &&
            !_shell.Confirm($"When this job printed, {entry.DataRecordCount:N0} record(s) were loaded; now there are {_session.RecordCount:N0}. " +
                            "The same sheet numbers may hold different records.\n\nContinue to the print dialog?"))
        {
            return false;
        }

        _session.Project.PrintSettings.CopyFrom(request.BuildSettings());
        _activeTab.HasConfiguredPrintSettings = true;
        StatusText = request.RemainingOnly
            ? $"Ready to print sheets {entry.ResumeSheet:N0}–{entry.LastSheet:N0} of \"{entry.ProjectTitle}\""
            : $"Reprint of \"{entry.ProjectTitle}\" ready";

        try
        {
            if (_shell.ShowPrintDialog())
            {
                StatusText = "Sent to printer";
                _env.SaveSettings();
                return true;
            }
        }
        catch (Exception ex)
        {
            _shell.Warn("Printing failed:\n\n" + ex.Message);
        }

        return false;
    }

    private void OpenPrintSettingsThenPrint()
    {
        if (_shell.ShowPrintSettings())
        {
            _activeTab.HasConfiguredPrintSettings = true;
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

        PrintLog? log = null;
        CountingProgress? counter = null;
        try
        {
            // The PDF follows the same print settings as a printed job (start slot,
            // skipped slots, record range, sheet range, outlines), so it is a faithful proof.
            PrintSettings settings = _session.Project.PrintSettings;
            if (settings.Content == PrintContent.VariableData && (_session.Data?.RowCount ?? 0) == 0)
            {
                settings.Content = PrintContent.SheetFill;
            }

            BulkRenderPlan plan = SheetLayoutEngine.CreatePlan(_session.Project, _session.Data, settings);
            var allSheets = SheetLayoutEngine.PlanSheets(plan).ToList();
            (IReadOnlyList<PlannedSheet> slice, int firstSheet) = settings.ApplySheetRange<PlannedSheet>(allSheets);
            PrintJobSummary summary = PrintJobSummary.FromSheets(allSheets, slice, firstSheet);
            int sheets = summary.Sheets;

            var request = new ExportRequest
            {
                Project = _session.Project,
                Data = plan.Data,
                Template = plan.Template,
                Scope = ExportScope.Sheet,
                Title = _session.Project.Metadata.Title,
                StartPosition = plan.StartPosition,
                SkipPositions = plan.SkipPositions,
                RecordSelection = plan.RecordSelection,
                FillSheetWhenNoData = plan.FillSheetWhenNoData,
                DrawLabelOutlines = settings.PrintLabelOutlines,
                DrawCropMarks = settings.PrintCropMarks,
                FirstSheet = settings.HasSheetRange ? firstSheet : null,
                LastSheet = settings.HasSheetRange ? firstSheet + Math.Max(0, sheets - 1) : null,
            };
            AssetStore assets = _session.Document.Assets;

            log = PrintLog.Start(_env.PrintHistory, _session, settings, summary, PrintHistoryKind.Pdf, null, path);

            // Rendering and writing run off the UI thread; the progress window is modal,
            // so the project cannot change underneath the export.
            _shell.RunJob($"Exporting {sheets:N0} sheet{(sheets == 1 ? "" : "s")} to PDF", "sheets rendered", sheets,
                (progress, token) => Task.Run(() =>
                {
                    byte[] pdf = new PdfExporter().Export(request, assets, counter = new CountingProgress(progress), token);
                    token.ThrowIfCancellationRequested();
                    File.WriteAllBytes(path, pdf);
                    return sheets;
                }, token));

            log.Completed();
            settings.FirstSheet = null;
            settings.LastSheet = null;
            StatusText = $"Exported {sheets:N0} sheet(s) to {Path.GetFileName(path)}";
        }
        catch (OperationCanceledException)
        {
            log?.Cancelled(counter?.Max ?? 0);
            StatusText = "PDF export cancelled";
        }
        catch (Exception ex)
        {
            log?.Failed(counter?.Max ?? 0, ex);
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
        _activeTab?.RefreshHeader();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SelectionInfo));
        OnPropertyChanged(nameof(DocumentTabTitle));
        OnPropertyChanged(nameof(SaveStateText));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(SheetSummary));
        OnPropertyChanged(nameof(TemplateSummary));
        OnPropertyChanged(nameof(SharedGuides));
        OnPropertyChanged(nameof(LockGuides));
        RebuildObjectList();
        RefreshCanvas();
        RaiseAllCanExecute();
    }

    private void OnSelectionChanged()
    {
        SyncObjectListSelection();
        OnPropertyChanged(nameof(SelectionInfo));
        _properties.RefreshAll();
        RefreshCanvas();
        RaiseAllCanExecute();
    }

    private void RaiseAllCanExecute()
    {
        // Every command, not a hand-kept list: the list went stale as commands were
        // added, leaving menu items greyed out (or live) after the selection changed.
        foreach (System.Reflection.PropertyInfo property in CommandProperties)
        {
            (property.GetValue(this) as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    private static readonly System.Reflection.PropertyInfo[] CommandProperties =
        typeof(MainViewModel).GetProperties().Where(p => p.PropertyType == typeof(RelayCommand)).ToArray();

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
}

public sealed record RecentEntry(string Path, string Title);

