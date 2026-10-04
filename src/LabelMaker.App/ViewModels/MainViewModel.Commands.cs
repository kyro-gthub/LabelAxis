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

/// <summary>Every command the window binds to, created in one place.</summary>
public sealed partial class MainViewModel
{
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

        AboutCommand = new RelayCommand(() => _shell.ShowAbout());
        BuildUpdateCommands();
        SettingsCommand = new RelayCommand(() => _shell.ShowSettings());
        ReplaceImageCommand = new RelayCommand(ReplaceSelectedImage, () => Properties.IsImage);
        ToggleObjectLockCommand = new RelayCommand(p => ToggleObjectLock(p as ObjectListItem));
    }

    private bool AnySel() => _session.Selection.Count > 0;
    private bool MultiSel() => _session.Selection.Count > 1;
}
