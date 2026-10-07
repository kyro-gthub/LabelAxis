using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LabelMaker.App.Rendering;
using LabelMaker.App.Services;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Settings;
using LabelMaker.Core.Units;

namespace LabelMaker.App.Controls;

/// <summary>
/// The WYSIWYG design surface. Renders the active label through the shared
/// <see cref="LabelMaker.Core.Rendering.LabelRenderer"/> (so it matches print),
/// then overlays rulers, ruler guides, snap lines and selection handles.
///
/// Navigation: wheel = zoom to cursor · middle / Space+left / empty-space drag =
/// pan · Ctrl+0 actual size · Ctrl+1 fit · arrows nudge (Shift ×10) or pan.
///
/// Transforms, all sampled live so a modifier can be pressed mid-drag:
/// drag a handle = resize · Shift = keep proportions · Ctrl = resize about the
/// centre · Alt = rotate (Shift snaps to 15°) · Shift while moving = constrain to
/// one axis.
///
/// Guides (spec §2): drag from the top or left ruler to drop a guide; drag a
/// guide to move it; drag it back onto a ruler to delete it. Objects snap their
/// edges / centres to guides, to other objects, and to the label edges / centre
/// while moving or resizing.
/// </summary>
public sealed partial class DesignerCanvas : FrameworkElement
{
    private const double K = UnitConverter.DiuPerInch / UnitConverter.MmPerInch; // DIU per mm
    private const double HandleSize = 8.0;
    private const double RulerSize = 22.0;
    private const double GuideHitPx = 4.0;
    private const double MinZoom = 0.05;
    private const double MaxZoom = 60.0;

    // The theme's accent: selection outlines and handles follow the theme.
    private static Color Accent => Services.ThemeManager.Canvas.Accent;
    private static readonly Color GuideColor = Color.FromRgb(0x12, 0xB5, 0xC9);
    private static readonly Color SnapColor = Color.FromRgb(0xFF, 0x2E, 0x88);

    private readonly SheetVisualRenderer _renderer;
    private DocumentSession? _session;

    private double _zoom = 1.0;
    private Vector _pan;
    private bool _viewInitialised;

    // interaction state
    private bool _spaceDown;
    private bool _panning;
    private Point _panStartScreen;
    private Vector _panStartValue;
    private bool _dragging;
    private bool _resizing;
    private bool _rotating;
    private Handle _activeHandle = Handle.None;
    private Point _dragStartMm;
    private Dictionary<string, RectMm> _dragStartBounds = new();

    // Natural-size barcodes' settings when a drag began: a resize rescales the bars from these.
    private Dictionary<string, LabelMaker.Core.Barcodes.BarcodeSpec> _dragStartSpecs = new();
    private Dictionary<string, double> _dragStartRotation = new();
    private Dictionary<string, (double X, double Y)> _dragStartSkew = new();

    // Grow-to-fit text boxes: how much taller than their text each was when the drag began.
    private Dictionary<string, double> _dragStartTextRoom = new();

    // Resizing a whole group: its box when the drag began; members scale inside it.
    private RectMm? _groupResizeStart;
    private double _rotateStartAngle;
    private bool _skewing;

    // drag-to-select
    private bool _marquee;
    private Point _marqueeStart;
    private Point _marqueeEnd;
    private IReadOnlyList<LabelObject> _marqueeBase = Array.Empty<LabelObject>();

    private const double RotateKnobPx = 26.0;
    private static readonly System.Windows.Media.DashStyle MarqueeDash = new(new[] { 3.0, 2.0 }, 0);
    private static readonly System.Windows.Media.DashStyle FreeTransformDash = new(new[] { 4.0, 3.0 }, 0);
    private const double MarqueeThresholdPx = 3.0;

    private bool _freeTransform;

    /// <summary>
    /// Free-transform mode: side handles skew instead of stretch, and the rotate knob
    /// is the primary way to turn the object. Ctrl+Alt on a side handle skews in the
    /// normal mode too.
    /// </summary>
    public bool FreeTransform
    {
        get => _freeTransform;
        set
        {
            _freeTransform = value;
            InvalidateVisual();
            FreeTransformChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? FreeTransformChanged;
    private IReadOnlyList<SnapLine> _activeSnapLines = Array.Empty<SnapLine>();

    private GuideDrag? _guideDrag;

    public DesignerCanvas(SheetVisualRenderer renderer)
    {
        _renderer = renderer;
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        AllowDrop = true;

        // The rulers, workspace and selection are drawn in the theme's colours.
        Services.ThemeManager.Changed += (_, _) => InvalidateVisual();
    }

    /// <summary>Corner rounding of the canvas (the rounded canvas well of the main window).</summary>
    public double CornerRadius { get; set; }

    public event EventHandler? ZoomChanged;

    /// <summary>Raised when one or more image files are dropped onto the canvas from Explorer.</summary>
    public event EventHandler<ImagesDroppedEventArgs>? ImagesDropped;

    public AppSettings? Settings { get; set; }

    public double Zoom => _zoom;

    /// <summary>Document-mm position of the last right-click, for the context menu (paste / add guide).</summary>
    public PointMm? ContextMm { get; private set; }

    private Point? _contextScreen;

    /// <summary>Zoom and scroll position, kept per document tab so switching back returns to the same view.</summary>
    public readonly record struct ViewState(double Zoom, Vector Pan);

    /// <summary>The current view, or null before the canvas has been laid out.</summary>
    public ViewState? CaptureView() => _viewInitialised ? new ViewState(_zoom, _pan) : null;

    /// <summary>Shows <paramref name="session"/>, restoring <paramref name="view"/> when given, or fitting the label otherwise.</summary>
    public void Attach(DocumentSession session, ViewState? view)
    {
        Attach(session);
        if (view is { } v)
        {
            _zoom = Math.Clamp(v.Zoom, MinZoom, MaxZoom);
            _pan = v.Pan;
            _viewInitialised = true;
            ZoomChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    public void Attach(DocumentSession session)
    {
        CancelCustomShape();
        _editShape = null;
        if (_session is not null)
        {
            _session.DocumentChanged -= OnSessionChanged;
            _session.SelectionChanged -= OnSessionChanged;
            _session.RecordChanged -= OnSessionChanged;
        }

        _session = session;
        _session.DocumentChanged += OnSessionChanged;
        _session.SelectionChanged += OnSessionChanged;
        _session.RecordChanged += OnSessionChanged;

        _viewInitialised = false;
        if (ActualWidth > 0 && ActualHeight > 0)
        {
            FitToView();
        }

        InvalidateVisual();
    }

    public void ClearGuides()
    {
        if (_session is null || _session.ActiveGuides.IsEmpty)
        {
            return;
        }

        var guides = _session.ActiveGuides;
        _session.Execute(new GuideEditCommand(guides, "Clear guides", guides.Clear));
        InvalidateVisual();
    }

    /// <summary>True when the last right-click landed on an existing guide; reports which one.</summary>
    public bool TryFindGuideAtContext(out bool vertical, out int index)
    {
        vertical = false;
        index = -1;
        if (_session is null || _contextScreen is not { } screen || !TryHitGuide(screen, out GuideDrag g))
        {
            return false;
        }

        vertical = g.Vertical;
        index = g.Index;
        return true;
    }

    /// <summary>Remove a single guide line (used by the canvas context menu's "Remove guide").</summary>
    public void RemoveGuideAt(bool vertical, int index)
    {
        if (_session is null || _session.ActiveGuides.Locked)
        {
            return;
        }

        var guides = _session.ActiveGuides;
        List<double> list = vertical ? guides.VerticalMm : guides.HorizontalMm;
        if (index < 0 || index >= list.Count)
        {
            return;
        }

        _session.Execute(new GuideEditCommand(guides, "Remove guide", () => list.RemoveAt(index)));
        InvalidateVisual();
    }

    /// <summary>Add a guide at the last right-click position (used by the canvas context menu).</summary>
    public void InsertGuideAtContext(bool vertical)
    {
        if (_session is null || ContextMm is not { } at || _session.ActiveGuides.Locked)
        {
            return;
        }

        var guides = _session.ActiveGuides;
        double max = vertical ? LabelWidthMm : LabelHeightMm;
        double value = Math.Round(Math.Clamp(vertical ? at.X : at.Y, 0, max), 2);
        _session.Execute(new GuideEditCommand(guides, "Add guide", () =>
        {
            (vertical ? guides.VerticalMm : guides.HorizontalMm).Add(value);
            (vertical ? guides.VerticalMm : guides.HorizontalMm).Sort();
        }));
        InvalidateVisual();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        if (_session is null)
        {
            base.OnMouseRightButtonDown(e);
            return;
        }

        Point screen = e.GetPosition(this);
        _contextScreen = screen;
        if (screen.X > RulerPad && screen.Y > RulerPad)
        {
            Point mm = ScreenToMm(screen);
            ContextMm = new PointMm(mm.X, mm.Y);

            LabelObject? hit = HitObject(mm);
            if (hit is not null && !_session.Selection.Contains(hit))
            {
                _session.SetSelection(new[] { hit });
            }
        }
        else
        {
            ContextMm = null;
        }

        base.OnMouseRightButtonDown(e);
    }

    private void OnSessionChanged(object? sender, EventArgs e) => InvalidateVisual();

    // ---- drag-and-drop of image files from Explorer -----------------------

    private static readonly string[] DroppableImageExtensions =
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp" };

    private static bool HasDroppableFiles(IDataObject data, out string[] paths)
    {
        paths = Array.Empty<string>();
        if (!data.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        if (data.GetData(DataFormats.FileDrop) is not string[] files)
        {
            return false;
        }

        paths = files.Where(f => DroppableImageExtensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())).ToArray();
        return paths.Length > 0;
    }

    protected override void OnDragEnter(DragEventArgs e) => OnDragOver(e);

    protected override void OnDragOver(DragEventArgs e)
    {
        e.Effects = _session is not null && HasDroppableFiles(e.Data, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        e.Handled = true;
        if (_session is null || !HasDroppableFiles(e.Data, out string[] paths))
        {
            return;
        }

        Point screen = e.GetPosition(this);
        Point mm = ScreenToMm(screen);
        ImagesDropped?.Invoke(this, new ImagesDroppedEventArgs(paths, new PointMm(mm.X, mm.Y)));
    }

}

/// <summary>Files dropped onto the canvas and the document-mm point they were dropped at.</summary>
public sealed class ImagesDroppedEventArgs : EventArgs
{
    public ImagesDroppedEventArgs(IReadOnlyList<string> paths, PointMm atMm)
    {
        Paths = paths;
        AtMm = atMm;
    }

    public IReadOnlyList<string> Paths { get; }

    public PointMm AtMm { get; }
}
