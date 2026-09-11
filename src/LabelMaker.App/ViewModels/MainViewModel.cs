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
        _session.DataChanged += (_, _) => { OnPropertyChanged(nameof(RecordText)); OnPropertyChanged(nameof(DataSourceSummary)); RefreshSearch(); };
        _session.HistoryChanged += (_, _) => RaiseAllCanExecute();
        _session.PagesChanged += (_, _) => RebuildPages();
        _session.ActivePageChanged += (_, _) => OnActivePageChanged();

        Properties = new SelectionProperties(_session);
        _canvas?.Attach(_session);

        RebuildPages();
        RebuildObjectList();
        RefreshSearch();
        OnDocumentChanged();
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
        Pages.Add(new PageListItem(null, "Master label", 0, IsMaster: true));
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

    public string RecordText => _session.RecordCount > 0
        ? $"{_session.RecordIndex + 1} / {_session.RecordCount}"
        : "— / —";

    public string ZoomText => _canvas is null ? "100%" : $"{Math.Round(_canvas.Zoom * 100)}%";

    /// <summary>0 = Templates tab, 1 = Pages tab, in the left panel.</summary>
    public int SelectedLeftTab { get => _selectedLeftTab; set => SetField(ref _selectedLeftTab, value); }

    public string DataSourceSummary => _session.RecordCount > 0
        ? $"{_session.RecordCount} record{(_session.RecordCount == 1 ? "" : "s")} loaded"
        : "No data source connected";

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

    public void NotifyAutoSaveSettingsChanged() => AutoSaveSettingsChanged?.Invoke(this, EventArgs.Empty);

    public bool LockGuides
    {
        get => _session.ActiveLabel.Guides.Locked;
        set
        {
            _session.ActiveLabel.Guides.Locked = value;
            _session.MarkDirty();
            OnPropertyChanged();
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
        ? "Master label"
        : _session.Project.Pages.FirstOrDefault(p => p.Id == _session.ActivePageId)?.Name ?? "Page";

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
        PasteCommand = new RelayCommand(PasteObjects, () => ObjectClipboard.HasContent);
        GroupCommand = new RelayCommand(GroupSelection, () => _session.Selection.Count > 1);
        UngroupCommand = new RelayCommand(UngroupSelection, () => _session.Selection.Any(o => o.GroupId is not null));

        AddPageCommand = new RelayCommand(() => _session.AddPage());
        DuplicatePageCommand = new RelayCommand(() => _session.DuplicateActivePage());
        RemovePageCommand = new RelayCommand(
            () => { if (_shell.Confirm($"Remove page \"{ActivePageName}\"?")) _session.RemoveActivePage(); },
            () => !_session.IsEditingMaster);
        RenamePageCommand = new RelayCommand(RenameActivePage, () => !_session.IsEditingMaster);
        AddVerticalGuideCommand = new RelayCommand(() => _canvas?.InsertGuideAtContext(vertical: true));
        AddHorizontalGuideCommand = new RelayCommand(() => _canvas?.InsertGuideAtContext(vertical: false));

        AddTextCommand = new RelayCommand(() => AddObject(new TextObject { Content = RichTextContent.FromPlainText("Text"), Bounds = NewBounds(40, 8) }));
        AddBarcodeCommand = new RelayCommand(() => AddObject(new BarcodeObject
        {
            Bounds = NewBounds(40, 16),
            AutoSize = false,
            Spec = new BarcodeSpec { Symbology = BarcodeSymbology.Code128, Data = "12345678", ModuleWidthMm = 0.3, BarHeightMm = 12 },
        }));
        AddImageCommand = new RelayCommand(AddImage);
        AddRectangleCommand = new RelayCommand(() => AddObject(new ShapeObject { Shape = ShapeKind.Rectangle, Bounds = NewBounds(30, 20), Stroke = ColorRgba.Black, StrokeWidthMm = 0.25 }));
        AddRoundRectCommand = new RelayCommand(() => AddObject(new ShapeObject { Shape = ShapeKind.RoundRectangle, CornerRadiusMm = 2, Bounds = NewBounds(30, 20), Stroke = ColorRgba.Black, StrokeWidthMm = 0.25 }));
        AddEllipseCommand = new RelayCommand(() => AddObject(new ShapeObject { Shape = ShapeKind.Ellipse, Bounds = NewBounds(24, 24), Stroke = ColorRgba.Black, StrokeWidthMm = 0.25 }));
        AddLineCommand = new RelayCommand(() => AddObject(new ShapeObject { Shape = ShapeKind.Line, Bounds = NewBounds(40, 0.5), Stroke = ColorRgba.Black, StrokeWidthMm = 0.3 }));
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
        ClearDataCommand = new RelayCommand(() => _session.SetData(null, null), () => _session.Data is not null);
        SequencesCommand = new RelayCommand(() => _shell.ShowSequenceManager());
        DuplicateCheckCommand = new RelayCommand(DuplicateCheck);
        BulkBarcodeCommand = new RelayCommand(() => _shell.ShowBulkBarcode());
        BarcodeImageCommand = new RelayCommand(() => _shell.ShowBarcodeImage());
        PreflightCommand = new RelayCommand(RunPreflight);
        TemplateManagerCommand = new RelayCommand(() => _shell.ShowTemplateManager());

        PrintCommand = new RelayCommand(Print);
        PrintSettingsCommand = new RelayCommand(OpenPrintSettingsThenPrint);
        PrintPreviewCommand = new RelayCommand(Print); // system dialog offers preview
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
            "Label Maker 1.0\n\nWindows-first label design & printing.\nFormtec templates · barcodes · variable data · accurate printing.",
            "About Label Maker"));
        SettingsCommand = new RelayCommand(() => _shell.ShowSettings());
        ReplaceImageCommand = new RelayCommand(ReplaceSelectedImage, () => Properties.IsImage);
        ToggleObjectLockCommand = new RelayCommand(p => ToggleObjectLock(p as ObjectListItem));
    }

    private bool AnySel() => _session.Selection.Count > 0;
    private bool MultiSel() => _session.Selection.Count > 1;

    // ---------- object operations ----------

    private RectMm NewBounds(double w, double h)
    {
        var size = _session.ActiveLabel.SizeMm;
        return new RectMm(Math.Max(1, (size.Width - w) / 2), Math.Max(1, (size.Height - h) / 2), w, h);
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

    private void ToggleObjectLock(ObjectListItem? item)
    {
        if (item is null)
        {
            return;
        }

        LabelObject obj = item.Object;
        _session.Execute(new SetPropertyCommand<bool>("Toggle lock", () => obj.Locked, v => obj.Locked = v, !obj.Locked, (obj.Id, "locked")));
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

    private void PasteObjects()
    {
        var atMm = _canvas?.ContextMm;
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

    private void RenameActivePage()
    {
        if (_session.IsEditingMaster)
        {
            return;
        }

        string current = ActivePageName;
        string? name = _shell.Prompt("Rename page", "Page name:", current);
        if (!string.IsNullOrWhiteSpace(name) && name != current)
        {
            _session.RenameActivePage(name.Trim());
            OnPropertyChanged(nameof(ActivePageName));
        }
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

            if (_printServer.Print(_session.Project, _session.Data, settings))
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
