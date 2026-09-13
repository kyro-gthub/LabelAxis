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
public sealed class DesignerCanvas : FrameworkElement
{
    private const double K = UnitConverter.DiuPerInch / UnitConverter.MmPerInch; // DIU per mm
    private const double HandleSize = 8.0;
    private const double RulerSize = 22.0;
    private const double GuideHitPx = 4.0;
    private const double MinZoom = 0.05;
    private const double MaxZoom = 60.0;

    private static readonly Color Accent = Color.FromRgb(0x2C, 0x6F, 0xE8);
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
    private Dictionary<string, double> _dragStartRotation = new();
    private Dictionary<string, (double X, double Y)> _dragStartSkew = new();

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
    }

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
        if (_session is null || _session.ActiveLabel.Guides.IsEmpty)
        {
            return;
        }

        var guides = _session.ActiveLabel.Guides;
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
        if (_session is null || _session.ActiveLabel.Guides.Locked)
        {
            return;
        }

        var guides = _session.ActiveLabel.Guides;
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
        if (_session is null || ContextMm is not { } at || _session.ActiveLabel.Guides.Locked)
        {
            return;
        }

        var guides = _session.ActiveLabel.Guides;
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

    // ---- view transform ---------------------------------------------------

    private double RulerPad => (Settings?.ShowRulers ?? true) ? RulerSize : 0;

    private double LabelWidthMm => _session?.ActiveLabel.SizeMm.Width ?? 100;
    private double LabelHeightMm => _session?.ActiveLabel.SizeMm.Height ?? 60;

    private double Scale => _zoom * K;

    /// <summary>Screen DIU per document millimetre at the current zoom.</summary>
    public double ScreenScale => Scale;

    /// <summary>An object's un-rotated box in this control's coordinates (for overlays such as the text editor).</summary>
    public Rect ObjectScreenRect(LabelObject obj)
    {
        Point a = MmToScreen(obj.Bounds.X, obj.Bounds.Y);
        Point b = MmToScreen(obj.Bounds.Right, obj.Bounds.Bottom);
        return new Rect(a, b);
    }

    /// <summary>Raised on double-click (or Enter / F2) on a text object.</summary>
    public event EventHandler<LabelObject>? EditTextRequested;

    /// <summary>Also raised when the view pans, so overlays can follow or close.</summary>
    public event EventHandler? ViewChanged;

    private string? _hiddenObjectId;

    /// <summary>An object left out of the rendering — the one being edited in place, so it isn't drawn twice.</summary>
    public string? HiddenObjectId
    {
        get => _hiddenObjectId;
        set
        {
            _hiddenObjectId = value;
            InvalidateVisual();
        }
    }

    private Point ContentOrigin
    {
        get
        {
            double pad = RulerPad;
            return new Point(
                pad + ((ActualWidth - pad - (LabelWidthMm * Scale)) / 2.0) + _pan.X,
                pad + ((ActualHeight - pad - (LabelHeightMm * Scale)) / 2.0) + _pan.Y);
        }
    }

    private Point ScreenToMm(Point p)
    {
        Point o = ContentOrigin;
        return new Point((p.X - o.X) / Scale, (p.Y - o.Y) / Scale);
    }

    private Point MmToScreen(double xMm, double yMm)
    {
        Point o = ContentOrigin;
        return new Point(o.X + (xMm * Scale), o.Y + (yMm * Scale));
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        if (!_viewInitialised && ActualWidth > 0 && ActualHeight > 0)
        {
            FitToView();
        }
    }

    public void FitToView()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        double pad = RulerPad;
        const double margin = 48;
        double sx = (ActualWidth - pad - (margin * 2)) / (LabelWidthMm * K);
        double sy = (ActualHeight - pad - (margin * 2)) / (LabelHeightMm * K);
        _zoom = Math.Clamp(Math.Min(sx, sy), MinZoom, MaxZoom);
        _pan = default;
        _viewInitialised = true;
        InvalidateVisual();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ZoomToActualSize()
    {
        double screenDpi = Settings?.ScreenDpiCalibration ?? UnitConverter.DiuPerInch;
        ZoomToward(Math.Clamp(screenDpi / UnitConverter.DiuPerInch, MinZoom, MaxZoom), Centre);
    }

    public void ZoomIn() => ZoomAt(1.2, Centre);

    public void ZoomOut() => ZoomAt(1 / 1.2, Centre);

    private Point Centre => new(ActualWidth / 2.0, ActualHeight / 2.0);

    private void ZoomAt(double factor, Point anchor) =>
        ZoomToward(Math.Clamp(_zoom * factor, MinZoom, MaxZoom), anchor);

    private void ZoomToward(double newZoom, Point anchorScreen)
    {
        if (Math.Abs(newZoom - _zoom) < 1e-6)
        {
            return;
        }

        Point mm = ScreenToMm(anchorScreen);
        _zoom = newZoom;
        double pad = RulerPad;
        _pan = new Vector(
            anchorScreen.X - (mm.X * Scale) - pad - ((ActualWidth - pad - (LabelWidthMm * Scale)) / 2.0),
            anchorScreen.Y - (mm.Y * Scale) - pad - ((ActualHeight - pad - (LabelHeightMm * Scale)) / 2.0));
        _viewInitialised = true;
        InvalidateVisual();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- rendering ------------------------------------------------------

    private static bool IsDark => Services.ThemeManager.Current == Services.AppTheme.Dark;

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(IsDark ? Color.FromRgb(0x15, 0x16, 0x20) : Color.FromRgb(0xE9, 0xEE, 0xF4)), null, new Rect(RenderSize));
        if (_session is null)
        {
            return;
        }

        LabelDocument label = _session.ActiveLabel;
        LabelDocument rendered = _hiddenObjectId is null
            ? label
            : new LabelDocument
            {
                SizeMm = label.SizeMm,
                Background = label.Background,
                Objects = label.Objects.Where(o => o.Id != _hiddenObjectId).ToList(),
            };

        var visual = _renderer.RenderSingleLabel(
            rendered,
            _session.CurrentResolveContext(),
            _renderer.OptionsFor(_session.Project.Template ?? Fallback(), LabelMaker.Core.Rendering.LabelOutlineStyle.Hairline),
            ColorRgba.White);

        Point origin = ContentOrigin;
        dc.PushTransform(new TranslateTransform(origin.X, origin.Y));
        dc.PushTransform(new ScaleTransform(_zoom, _zoom));

        var paperRect = new Rect(0, 0, label.SizeMm.Width * K, label.SizeMm.Height * K);
        double sh = 10 / _zoom;
        dc.DrawRectangle(new SolidColorBrush(IsDark ? Color.FromArgb(90, 0, 0, 0) : Color.FromArgb(28, 40, 30, 90)), null,
            new Rect(paperRect.X + sh, paperRect.Y + sh, paperRect.Width, paperRect.Height));
        dc.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(IsDark ? Color.FromRgb(0x3A, 0x3D, 0x4A) : Color.FromRgb(0xCE, 0xD1, 0xDE)), 1.0 / _zoom), paperRect);
        dc.DrawDrawing(visual.Drawing);

        if (Settings?.ShowGrid == true)
        {
            DrawGrid(dc, label, Settings.GridSizeMm);
        }

        DrawSelection(dc);

        dc.Pop();
        dc.Pop();

        if (Settings?.ShowGuides != false)
        {
            DrawGuides(dc, label);
        }

        DrawSnapLines(dc);

        if (_marquee)
        {
            Rect r = MarqueeRect;
            if (r.Width >= MarqueeThresholdPx || r.Height >= MarqueeThresholdPx)
            {
                var fill = new SolidColorBrush(Color.FromArgb(28, Accent.R, Accent.G, Accent.B));
                var pen = new Pen(new SolidColorBrush(Accent), 1) { DashStyle = MarqueeDash };
                dc.DrawRectangle(fill, pen, r);
            }
        }

        if (RulerPad > 0)
        {
            DrawRulers(dc, label);
        }
    }

    private void DrawGrid(DrawingContext dc, LabelDocument label, double stepMm)
    {
        if (stepMm <= 0 || stepMm * Scale < 4)
        {
            return;
        }

        var pen = new Pen(new SolidColorBrush(Color.FromArgb(30, 44, 111, 232)), 0.6 / _zoom);
        pen.Freeze();
        for (double x = 0; x <= label.SizeMm.Width + 1e-6; x += stepMm)
        {
            dc.DrawLine(pen, new Point(x * K, 0), new Point(x * K, label.SizeMm.Height * K));
        }

        for (double y = 0; y <= label.SizeMm.Height + 1e-6; y += stepMm)
        {
            dc.DrawLine(pen, new Point(0, y * K), new Point(label.SizeMm.Width * K, y * K));
        }
    }

    private void DrawSelection(DrawingContext dc)
    {
        if (_session is null)
        {
            return;
        }

        var stroke = new Pen(new SolidColorBrush(Accent), 1.3 / _zoom);
        if (_freeTransform)
        {
            stroke.DashStyle = FreeTransformDash;
        }

        stroke.Freeze();
        var handlePen = new Pen(new SolidColorBrush(Accent), 1.3 / _zoom);
        handlePen.Freeze();
        double hs = HandleSize / _zoom;

        if (SelectedGroupBounds() is { } group)
        {
            // Members get a faint outline; the group itself has one box and one set of handles.
            var memberPen = new Pen(new SolidColorBrush(Color.FromArgb(0x90, Accent.R, Accent.G, Accent.B)), 0.8 / _zoom)
            {
                DashStyle = MarqueeDash,
            };
            memberPen.Freeze();
            foreach (LabelObject obj in _session.Selection)
            {
                PointMm[] c = ObjectGeometry.Corners(obj);
                var g = new StreamGeometry();
                using (StreamGeometryContext ctx = g.Open())
                {
                    ctx.BeginFigure(ToCanvas(c[0]), false, true);
                    ctx.PolyLineTo(c.Skip(1).Select(ToCanvas).ToList(), true, true);
                }

                g.Freeze();
                dc.DrawGeometry(null, memberPen, g);
            }

            dc.DrawRectangle(null, stroke, new Rect(group.X * K, group.Y * K, group.Width * K, group.Height * K));
            if (!_session.Selection.Any(o => o.Locked))
            {
                foreach ((Handle _, PointMm p) in RectHandles(group))
                {
                    Point c = ToCanvas(p);
                    dc.DrawRectangle(Brushes.White, handlePen, new Rect(c.X - (hs / 2), c.Y - (hs / 2), hs, hs));
                }
            }

            return;
        }

        foreach (LabelObject obj in _session.Selection)
        {
            // Outline and handles follow the real (rotated, skewed) shape.
            PointMm[] corners = ObjectGeometry.Corners(obj);
            var outline = new StreamGeometry();
            using (StreamGeometryContext g = outline.Open())
            {
                g.BeginFigure(ToCanvas(corners[0]), false, true);
                g.PolyLineTo(corners.Skip(1).Select(ToCanvas).ToList(), true, true);
            }

            outline.Freeze();
            dc.DrawGeometry(null, stroke, outline);

            if (obj.Locked)
            {
                continue;
            }

            foreach ((Handle handle, PointMm local) in HandleLocals(obj))
            {
                Point p = ToCanvas(ObjectGeometry.ToWorld(obj, local));
                bool side = handle is Handle.Top or Handle.Bottom or Handle.Left or Handle.Right;

                if (handle == Handle.Rotate)
                {
                    Point top = ToCanvas(ObjectGeometry.ToWorld(obj, new PointMm(0, -obj.Bounds.Height / 2.0)));
                    dc.DrawLine(handlePen, top, p);
                    dc.DrawEllipse(Brushes.White, handlePen, p, hs * 0.65, hs * 0.65);
                }
                else if (side && _freeTransform)
                {
                    // Diamonds mark the skew handles in free-transform mode.
                    var diamond = new StreamGeometry();
                    using (StreamGeometryContext g = diamond.Open())
                    {
                        g.BeginFigure(new Point(p.X, p.Y - (hs * 0.7)), true, true);
                        g.PolyLineTo(new[]
                        {
                            new Point(p.X + (hs * 0.7), p.Y), new Point(p.X, p.Y + (hs * 0.7)), new Point(p.X - (hs * 0.7), p.Y),
                        }, true, true);
                    }

                    diamond.Freeze();
                    dc.DrawGeometry(Brushes.White, handlePen, diamond);
                }
                else
                {
                    dc.DrawRectangle(Brushes.White, handlePen, new Rect(p.X - (hs / 2), p.Y - (hs / 2), hs, hs));
                }
            }
        }
    }

    // ---- groups ---------------------------------------------------------------------

    /// <summary>The objects plus every other member of any group they belong to.</summary>
    private IReadOnlyList<LabelObject> WithGroups(IEnumerable<LabelObject> objects)
    {
        var result = new List<LabelObject>();
        foreach (LabelObject obj in objects)
        {
            IEnumerable<LabelObject> members = obj.GroupId is { } id
                ? _session!.ActiveLabel.GroupMembers(id).Where(m => m.Visible)
                : new[] { obj };
            result.AddRange(members.Where(m => !result.Contains(m)));
        }

        return result;
    }

    /// <summary>
    /// When the selection is exactly one group, the box around all its members (their
    /// real rotated shapes); otherwise null.
    /// </summary>
    private RectMm? SelectedGroupBounds()
    {
        if (_session is not { Selection.Count: > 1 } s || s.Selection[0].GroupId is not { } id ||
            s.Selection.Any(o => o.GroupId != id) ||
            s.ActiveLabel.GroupMembers(id).Count(m => m.Visible) != s.Selection.Count)
        {
            return null;
        }

        RectMm box = ObjectGeometry.Bounds(s.Selection[0]);
        foreach (LabelObject obj in s.Selection.Skip(1))
        {
            RectMm b = ObjectGeometry.Bounds(obj);
            box = RectMm.FromLtrb(Math.Min(box.X, b.X), Math.Min(box.Y, b.Y), Math.Max(box.Right, b.Right), Math.Max(box.Bottom, b.Bottom));
        }

        return box;
    }

    /// <summary>Objects that show their own handles: none when a whole group is selected.</summary>
    private IEnumerable<LabelObject> SelectionWithHandles() =>
        SelectedGroupBounds() is null ? _session!.Selection : Array.Empty<LabelObject>();

    private static IEnumerable<(Handle Handle, PointMm Point)> RectHandles(RectMm r)
    {
        double cx = r.X + (r.Width / 2), cy = r.Y + (r.Height / 2);
        yield return (Handle.TopLeft, new PointMm(r.X, r.Y));
        yield return (Handle.Top, new PointMm(cx, r.Y));
        yield return (Handle.TopRight, new PointMm(r.Right, r.Y));
        yield return (Handle.Right, new PointMm(r.Right, cy));
        yield return (Handle.BottomRight, new PointMm(r.Right, r.Bottom));
        yield return (Handle.Bottom, new PointMm(cx, r.Bottom));
        yield return (Handle.BottomLeft, new PointMm(r.X, r.Bottom));
        yield return (Handle.Left, new PointMm(r.X, cy));
    }

    private Handle HitRectHandle(RectMm r, Point screen)
    {
        double tol = (HandleSize / 2.0) + 4;
        foreach ((Handle handle, PointMm p) in RectHandles(r))
        {
            Point s = MmToScreen(p.X, p.Y);
            if (Math.Abs(s.X - screen.X) <= tol && Math.Abs(s.Y - screen.Y) <= tol)
            {
                return handle;
            }
        }

        return Handle.None;
    }

    /// <summary>Scales every member's box from the group's starting box into <paramref name="to"/>.</summary>
    private void ResizeGroup(RectMm from, RectMm to)
    {
        double sx = from.Width > 0.001 ? to.Width / from.Width : 1;
        double sy = from.Height > 0.001 ? to.Height / from.Height : 1;
        foreach (LabelObject obj in _session!.Selection)
        {
            if (!_dragStartBounds.TryGetValue(obj.Id, out RectMm start))
            {
                continue;
            }

            // Keep each member's centre in the same relative place and scale its size.
            PointMm c = start.Center;
            var centre = new PointMm(to.X + ((c.X - from.X) * sx), to.Y + ((c.Y - from.Y) * sy));
            obj.Bounds = RectMm.FromCenter(centre, new SizeMm(Math.Max(0.2, start.Width * sx), Math.Max(0.2, start.Height * sy)));
        }
    }

    /// <summary>Document mm to the zoomed drawing space used inside OnRender's transforms.</summary>
    private static Point ToCanvas(PointMm mm) => new(mm.X * K, mm.Y * K);

    /// <summary>Where each handle sits in the object's local (centre-origin) space.</summary>
    private IEnumerable<(Handle Handle, PointMm Local)> HandleLocals(LabelObject obj)
    {
        double hw = obj.Bounds.Width / 2.0, hh = obj.Bounds.Height / 2.0;
        yield return (Handle.TopLeft, new PointMm(-hw, -hh));
        yield return (Handle.Top, new PointMm(0, -hh));
        yield return (Handle.TopRight, new PointMm(hw, -hh));
        yield return (Handle.Right, new PointMm(hw, 0));
        yield return (Handle.BottomRight, new PointMm(hw, hh));
        yield return (Handle.Bottom, new PointMm(0, hh));
        yield return (Handle.BottomLeft, new PointMm(-hw, hh));
        yield return (Handle.Left, new PointMm(-hw, 0));

        // A fixed on-screen distance above the top edge, whatever the zoom. The knob
        // is skipped for lines, which have no meaningful "top".
        if (obj is not LabelMaker.Core.Model.Objects.ShapeObject { Shape: LabelMaker.Core.Model.Objects.ShapeKind.Line })
        {
            yield return (Handle.Rotate, new PointMm(0, -hh - (RotateKnobPx / Scale)));
        }
    }

    private void DrawGuides(DrawingContext dc, LabelDocument label)
    {
        var pen = new Pen(new SolidColorBrush(GuideColor), 1.0) { DashStyle = DashStyles.Dash };
        pen.Freeze();

        double top = RulerPad;
        double left = RulerPad;

        foreach (double xMm in label.Guides.VerticalMm)
        {
            double x = MmToScreen(xMm, 0).X;
            if (x >= left)
            {
                dc.DrawLine(pen, new Point(x, top), new Point(x, ActualHeight));
            }
        }

        foreach (double yMm in label.Guides.HorizontalMm)
        {
            double y = MmToScreen(0, yMm).Y;
            if (y >= top)
            {
                dc.DrawLine(pen, new Point(left, y), new Point(ActualWidth, y));
            }
        }

        if (_guideDrag is { } g)
        {
            var live = new Pen(new SolidColorBrush(GuideColor), 1.4);
            live.Freeze();
            if (g.Vertical)
            {
                double x = MmToScreen(g.ValueMm, 0).X;
                dc.DrawLine(live, new Point(x, top), new Point(x, ActualHeight));
                DrawBadge(dc, new Point(x + 4, top + 4), $"{g.ValueMm:0.0} mm");
            }
            else
            {
                double y = MmToScreen(0, g.ValueMm).Y;
                dc.DrawLine(live, new Point(left, y), new Point(ActualWidth, y));
                DrawBadge(dc, new Point(left + 4, y + 4), $"{g.ValueMm:0.0} mm");
            }
        }
    }

    private void DrawSnapLines(DrawingContext dc)
    {
        if (_activeSnapLines.Count == 0)
        {
            return;
        }

        var pen = new Pen(new SolidColorBrush(SnapColor), 1.0);
        pen.Freeze();
        foreach (SnapLine line in _activeSnapLines)
        {
            if (line.Vertical)
            {
                double x = MmToScreen(line.PositionMm, 0).X;
                dc.DrawLine(pen, new Point(x, RulerPad), new Point(x, ActualHeight));
            }
            else
            {
                double y = MmToScreen(0, line.PositionMm).Y;
                dc.DrawLine(pen, new Point(RulerPad, y), new Point(ActualWidth, y));
            }
        }
    }

    private void DrawRulers(DrawingContext dc, LabelDocument label)
    {
        var bg = new SolidColorBrush(IsDark ? Color.FromRgb(0x1F, 0x20, 0x2C) : Color.FromRgb(0xF6, 0xF7, 0xFB));
        var line = new Pen(new SolidColorBrush(IsDark ? Color.FromRgb(0x33, 0x35, 0x4A) : Color.FromRgb(0xD6, 0xD9, 0xE4)), 1);
        var tick = new Pen(new SolidColorBrush(IsDark ? Color.FromRgb(0x7A, 0x7F, 0xA0) : Color.FromRgb(0x9A, 0x9F, 0xB5)), 1);
        var textBrush = new SolidColorBrush(IsDark ? Color.FromRgb(0xB5, 0xB8, 0xD0) : Color.FromRgb(0x6B, 0x70, 0x88));

        dc.DrawRectangle(bg, null, new Rect(0, 0, ActualWidth, RulerSize));
        dc.DrawRectangle(bg, null, new Rect(0, 0, RulerSize, ActualHeight));
        dc.DrawRectangle(bg, null, new Rect(0, 0, RulerSize, RulerSize));
        dc.DrawLine(line, new Point(0, RulerSize), new Point(ActualWidth, RulerSize));
        dc.DrawLine(line, new Point(RulerSize, 0), new Point(RulerSize, ActualHeight));

        double step = NiceStep();
        Point o = ContentOrigin;

        // horizontal ruler
        double startX = Math.Floor((RulerSize - o.X) / (step * Scale)) * step;
        for (double mm = startX; ; mm += step)
        {
            double x = o.X + (mm * Scale);
            if (x > ActualWidth)
            {
                break;
            }

            if (x < RulerSize)
            {
                continue;
            }

            dc.DrawLine(tick, new Point(x, RulerSize - 6), new Point(x, RulerSize));
            var ft = Label(RulerLabel(mm), textBrush);
            dc.DrawText(ft, new Point(x + 2, 2));
        }

        // vertical ruler
        double startY = Math.Floor((RulerSize - o.Y) / (step * Scale)) * step;
        for (double mm = startY; ; mm += step)
        {
            double y = o.Y + (mm * Scale);
            if (y > ActualHeight)
            {
                break;
            }

            if (y < RulerSize)
            {
                continue;
            }

            dc.DrawLine(tick, new Point(RulerSize - 6, y), new Point(RulerSize, y));
            var ft = Label(RulerLabel(mm), textBrush);
            dc.PushTransform(new RotateTransform(-90, RulerSize - 12, y - 2));
            dc.DrawText(ft, new Point(RulerSize - 12, y - 2));
            dc.Pop();
        }

        // cursor position markers
        Point cur = Mouse.GetPosition(this);
        var cursorPen = new Pen(new SolidColorBrush(Accent), 1);
        if (cur.X > RulerSize)
        {
            dc.DrawLine(cursorPen, new Point(cur.X, 0), new Point(cur.X, RulerSize));
        }

        if (cur.Y > RulerSize)
        {
            dc.DrawLine(cursorPen, new Point(0, cur.Y), new Point(RulerSize, cur.Y));
        }

        _ = label;
    }

    /// <summary>
    /// Ruler ticks are laid out in millimetres (the document unit) but labelled in
    /// whatever unit the user picked, so the marks stay on round physical positions.
    /// </summary>
    private string RulerLabel(double millimetres)
    {
        Unit unit = Settings?.DisplayUnit ?? Unit.Mm;
        double dpi = Math.Max(1, Settings?.ScreenDpiCalibration ?? 96);
        double value = UnitConverter.FromMillimetres(millimetres, unit, dpi);
        string format = unit switch
        {
            Unit.Cm => "0.#",
            Unit.Inch => "0.##",
            _ => "0",
        };

        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    private double NiceStep()
    {
        double[] steps = { 1, 2, 5, 10, 20, 50, 100, 200 };
        foreach (double s in steps)
        {
            if (s * Scale >= 46)
            {
                return s;
            }
        }

        return 500;
    }

    private static FormattedText Label(string text, Brush brush) => new(
        text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
        new Typeface("Segoe UI"), 9, brush, 1.0);

    private void DrawBadge(DrawingContext dc, Point at, string text)
    {
        var ft = Label(text, Brushes.White);
        var rect = new Rect(at.X, at.Y, ft.Width + 10, ft.Height + 4);
        dc.DrawRoundedRectangle(new SolidColorBrush(GuideColor), null, rect, 3, 3);
        dc.DrawText(ft, new Point(at.X + 5, at.Y + 2));
    }

    // ---- interaction ---------------------------------------------------

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        ZoomAt(e.Delta > 0 ? 1.15 : 1 / 1.15, e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        if (_session is null)
        {
            return;
        }

        Point screen = e.GetPosition(this);

        bool wantsPan = e.ChangedButton == MouseButton.Middle ||
                        (e.ChangedButton == MouseButton.Left && _spaceDown);
        if (wantsPan)
        {
            BeginPan(screen);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        // Ruler drag -> new guide (only the ruler strips themselves; never competes with objects)
        if (RulerPad > 0 && !(_session.ActiveLabel.Guides.Locked))
        {
            if (screen.Y < RulerSize && screen.X > RulerSize)
            {
                _guideDrag = new GuideDrag(false, -1, ScreenToMm(screen).Y);
                CaptureMouse();
                e.Handled = true;
                return;
            }

            if (screen.X < RulerSize && screen.Y > RulerSize)
            {
                _guideDrag = new GuideDrag(true, -1, ScreenToMm(screen).X);
                CaptureMouse();
                e.Handled = true;
                return;
            }
        }

        Point mm = ScreenToMm(screen);
        _dragStartMm = mm;

        // A selected group has one box with its own handles; they come first.
        if (SelectedGroupBounds() is { } groupBox && !_session.Selection.Any(o => o.Locked) &&
            HitRectHandle(groupBox, screen) is var gh and not Handle.None)
        {
            _resizing = true;
            _activeHandle = gh;
            _groupResizeStart = groupBox;
            CaptureStart();
            CaptureMouse();
            e.Handled = true;
            return;
        }

        // Handles on the current selection take priority over everything else.
        foreach (LabelObject obj in SelectionWithHandles())
        {
            Handle h = obj.Locked ? Handle.None : HitHandle(obj, screen);
            if (h == Handle.None)
            {
                continue;
            }

            bool side = h is Handle.Top or Handle.Bottom or Handle.Left or Handle.Right;
            bool ctrlAlt = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

            _resizing = true;
            _activeHandle = h;
            // The rotate knob always rotates; Alt turns any other handle into rotation.
            _rotating = h == Handle.Rotate || (Keyboard.Modifiers == ModifierKeys.Alt);
            // Side handles skew in free-transform mode, or with Ctrl+Alt in normal mode.
            _skewing = !_rotating && side && (_freeTransform || ctrlAlt);
            CaptureStart();
            _rotateStartAngle = AngleFromCentre(obj.Bounds.Center, mm);
            CaptureMouse();
            e.Handled = true;
            return;
        }

        // Object selection/drag beats grabbing a guide that happens to sit on the
        // object's edge (e.g. right after a successful snap) — otherwise a snapped
        // object becomes impossible to re-select at its snapped edge.
        LabelObject? hit = HitObject(mm);

        if (hit is null && Settings?.ShowGuides != false && !_session.ActiveLabel.Guides.Locked &&
            TryHitGuide(screen, out GuideDrag existing))
        {
            _guideDrag = existing;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (hit is null)
        {
            // Drag on empty space draws a selection box; Shift adds to what is selected.
            // Panning moved to Space+drag and the middle button, as in other design tools.
            bool additive = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            _marqueeBase = additive ? _session.Selection.ToList() : Array.Empty<LabelObject>();
            if (!additive)
            {
                _session.ClearSelection();
            }

            _marquee = true;
            _marqueeStart = _marqueeEnd = screen;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 2 && hit is TextObject && !hit.Locked)
        {
            _session.SetSelection(new[] { hit });
            EditTextRequested?.Invoke(this, hit);
            e.Handled = true;
            return;
        }

        // Clicking any member of a group picks up the whole group; Ctrl+click reaches
        // into the group for just that one object.
        bool member = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        IReadOnlyList<LabelObject> picked = member ? new[] { hit } : WithGroups(new[] { hit });

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            var sel = _session.Selection.ToList();
            if (picked.All(sel.Contains))
            {
                sel.RemoveAll(picked.Contains);
            }
            else
            {
                sel.AddRange(picked.Where(o => !sel.Contains(o)));
            }

            _session.SetSelection(sel);
        }
        else if (!_session.Selection.Contains(hit) || (member && _session.Selection.Count > 1))
        {
            _session.SetSelection(picked);
        }

        if (!hit.Locked)
        {
            _dragging = true;
            CaptureStart();
            CaptureMouse();
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        Point screen = e.GetPosition(this);

        if (_guideDrag is { } g)
        {
            Point mm = ScreenToMm(screen);
            _guideDrag = g with { ValueMm = Math.Round(g.Vertical ? mm.X : mm.Y, 2) };
            Cursor = g.Vertical ? Cursors.SizeWE : Cursors.SizeNS;
            InvalidateVisual();
            return;
        }

        if (_panning)
        {
            _pan = _panStartValue + (screen - _panStartScreen);
            ViewChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            return;
        }

        if (_marquee)
        {
            _marqueeEnd = screen;
            UpdateMarqueeSelection();
            InvalidateVisual();
            return;
        }

        UpdateCursor(screen);

        if (RulerPad > 0)
        {
            InvalidateVisual(); // keep the ruler cursor markers live
        }

        if (!_dragging && !_resizing)
        {
            return;
        }

        Point pMm = ScreenToMm(screen);
        double dx = pMm.X - _dragStartMm.X;
        double dy = pMm.Y - _dragStartMm.Y;

        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

        if (_resizing && _skewing)
        {
            SkewSelection(dx, dy, shift);
            InvalidateVisual();
            return;
        }

        if (_resizing && (_rotating || (alt && !ctrl)))
        {
            RotateSelection(pMm, shift);
            InvalidateVisual();
            return;
        }

        if (!_resizing && shift)
        {
            // Shift while moving constrains to the dominant axis.
            if (Math.Abs(dx) > Math.Abs(dy))
            {
                dy = 0;
            }
            else
            {
                dx = 0;
            }
        }

        if (_resizing && _groupResizeStart is { } groupStart)
        {
            ResizeGroup(groupStart, ResizeRect(groupStart, _activeHandle, dx, dy, proportional: shift, fromCentre: ctrl));
            InvalidateVisual();
            return;
        }

        foreach (LabelObject obj in _session.Selection)
        {
            if (obj.Locked || !_dragStartBounds.TryGetValue(obj.Id, out RectMm start))
            {
                continue;
            }

            obj.Bounds = _resizing
                ? ResizeInLocalSpace(obj, start, _activeHandle, dx, dy, proportional: shift, fromCentre: ctrl)
                : start.Offset(dx, dy);
        }

        // Snapping fights a constrained resize, and edge snapping only makes sense for
        // an upright box, so it applies to plain drags on axis-aligned objects.
        bool uprightSingle = _session.Selection.Count == 1 && ObjectGeometry.IsAxisAligned(_session.Selection[0]);
        _activeSnapLines = _resizing
            ? (shift || ctrl || !uprightSingle ? Array.Empty<SnapLine>() : ApplyResizeSnap())
            : ApplyMoveSnap();

        InvalidateVisual();
    }

    /// <summary>
    /// Resizes against the object's own axes, so dragging the right handle of a
    /// rotated or skewed box stretches it along its length and the opposite edge
    /// stays put on screen. For an upright box this is exactly the plain rectangle
    /// resize it replaces.
    /// </summary>
    private static RectMm ResizeInLocalSpace(LabelObject obj, RectMm start, Handle handle, double dx, double dy, bool proportional, bool fromCentre)
    {
        Affine linear = ObjectGeometry.Linear(obj);
        if (ObjectGeometry.IsAxisAligned(obj))
        {
            return ResizeRect(start, handle, dx, dy, proportional, fromCentre);
        }

        // Pointer movement expressed along the object's axes.
        PointMm localDelta = linear.Inverse().ApplyVector(dx, dy);

        // Resize a centre-origin copy of the starting box in local space.
        var local = new RectMm(-start.Width / 2.0, -start.Height / 2.0, start.Width, start.Height);
        RectMm resized = ResizeRect(local, handle, localDelta.X, localDelta.Y, proportional, fromCentre);

        // The local centre moved by this much; carry that back into document space.
        PointMm shift = linear.ApplyVector(resized.Center.X, resized.Center.Y);
        PointMm centre = new(start.Center.X + shift.X, start.Center.Y + shift.Y);
        return RectMm.FromCenter(centre, new SizeMm(resized.Width, resized.Height));
    }

    /// <summary>
    /// Dragging a side handle shears the object: top and bottom handles lean it
    /// sideways (skew X), left and right handles tip it up or down (skew Y). Shift
    /// snaps to 5° steps.
    /// </summary>
    private void SkewSelection(double dx, double dy, bool snap)
    {
        if (_session is null)
        {
            return;
        }

        foreach (LabelObject obj in _session.Selection)
        {
            if (obj.Locked || !_dragStartBounds.TryGetValue(obj.Id, out RectMm start) ||
                !_dragStartSkew.TryGetValue(obj.Id, out (double X, double Y) skew) ||
                !_dragStartRotation.TryGetValue(obj.Id, out double rotation))
            {
                continue;
            }

            // Measure the drag along the object's rotated (but un-skewed) axes.
            Affine rotate = ObjectGeometry.Linear(rotation, 0, 0);
            PointMm along = rotate.Inverse().ApplyVector(dx, dy);
            double height = Math.Max(1, start.Height);
            double width = Math.Max(1, start.Width);

            double tanX = Math.Tan(skew.X * Math.PI / 180.0);
            double tanY = Math.Tan(skew.Y * Math.PI / 180.0);

            // Skew is about the centre, which would move both edges. Shifting the
            // centre by half the drag keeps the opposite edge still, so the handle
            // under the pointer is the only thing that moves.
            PointMm centreShift = default;
            switch (_activeHandle)
            {
                case Handle.Bottom:
                    tanX += along.X / height;
                    centreShift = rotate.ApplyVector(along.X / 2.0, 0);
                    break;
                case Handle.Top:
                    tanX -= along.X / height;
                    centreShift = rotate.ApplyVector(along.X / 2.0, 0);
                    break;
                case Handle.Right:
                    tanY += along.Y / width;
                    centreShift = rotate.ApplyVector(0, along.Y / 2.0);
                    break;
                case Handle.Left:
                    tanY -= along.Y / width;
                    centreShift = rotate.ApplyVector(0, along.Y / 2.0);
                    break;
            }

            obj.Bounds = start.Offset(centreShift.X, centreShift.Y);

            double newX = Math.Atan(tanX) * 180.0 / Math.PI;
            double newY = Math.Atan(tanY) * 180.0 / Math.PI;
            if (snap)
            {
                newX = Math.Round(newX / 5.0) * 5.0;
                newY = Math.Round(newY / 5.0) * 5.0;
            }

            obj.SkewXDegrees = Math.Round(Math.Clamp(newX, -ObjectGeometry.MaxSkewDegrees, ObjectGeometry.MaxSkewDegrees), 1);
            obj.SkewYDegrees = Math.Round(Math.Clamp(newY, -ObjectGeometry.MaxSkewDegrees, ObjectGeometry.MaxSkewDegrees), 1);
        }
    }

    private Rect MarqueeRect => new(_marqueeStart, _marqueeEnd);

    private void UpdateMarqueeSelection()
    {
        if (_session is null)
        {
            return;
        }

        Rect r = MarqueeRect;
        if (r.Width < MarqueeThresholdPx && r.Height < MarqueeThresholdPx)
        {
            return;
        }

        Point a = ScreenToMm(r.TopLeft);
        Point b = ScreenToMm(r.BottomRight);
        RectMm box = RectMm.FromLtrb(a.X, a.Y, b.X, b.Y);

        IReadOnlyList<LabelObject> picked = WithGroups(_session.ActiveLabel.Objects
            .Where(o => o.Visible && ObjectGeometry.Intersects(o, box)));

        _session.SetSelection(_marqueeBase.Concat(picked.Where(o => !_marqueeBase.Contains(o))).ToList());
    }

    /// <summary>Alt-dragging a handle rotates about the object's centre; Shift snaps to 15°.</summary>
    private void RotateSelection(Point pointerMm, bool snapAngle)
    {
        if (_session is null)
        {
            return;
        }

        _rotating = true;
        foreach (LabelObject obj in _session.Selection)
        {
            if (obj.Locked || !_dragStartBounds.TryGetValue(obj.Id, out RectMm start) ||
                !_dragStartRotation.TryGetValue(obj.Id, out double startAngle))
            {
                continue;
            }

            double delta = AngleFromCentre(start.Center, pointerMm) - _rotateStartAngle;
            double angle = startAngle + delta;
            if (snapAngle)
            {
                angle = Math.Round(angle / 15.0) * 15.0;
            }

            obj.RotationDegrees = Math.Round(((angle % 360) + 360) % 360, 1);
        }
    }

    private static double AngleFromCentre(PointMm centre, Point p) =>
        Math.Atan2(p.Y - centre.Y, p.X - centre.X) * 180.0 / Math.PI;

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        Point screen = e.GetPosition(this);

        if (_guideDrag is { } g && _session is not null)
        {
            var guides = _session.ActiveLabel.Guides;
            bool droppedOnRuler = screen.X < RulerSize || screen.Y < RulerSize;

            _session.Execute(new GuideEditCommand(guides, "Edit guide", () =>
            {
                List<double> list = g.Vertical ? guides.VerticalMm : guides.HorizontalMm;
                if (g.Index >= 0 && g.Index < list.Count)
                {
                    list.RemoveAt(g.Index);
                }

                double max = g.Vertical ? LabelWidthMm : LabelHeightMm;
                if (!droppedOnRuler && g.ValueMm >= -2 && g.ValueMm <= max + 2)
                {
                    list.Add(Math.Clamp(g.ValueMm, 0, max));
                    list.Sort();
                }
            }));

            _guideDrag = null;
            ReleaseMouseCapture();
            InvalidateVisual();
            return;
        }

        if (_panning)
        {
            _panning = false;
            ReleaseMouseCapture();
            UpdateCursor(screen);
            return;
        }

        if (_marquee)
        {
            _marqueeEnd = screen;
            UpdateMarqueeSelection();
            _marquee = false;
            _marqueeBase = Array.Empty<LabelObject>();
            ReleaseMouseCapture();
            InvalidateVisual();
            return;
        }

        if (_session is not null && (_dragging || _resizing))
        {
            var commands = new List<IUndoableCommand>();
            foreach (LabelObject obj in _session.Selection)
            {
                if (!_dragStartBounds.TryGetValue(obj.Id, out RectMm start) ||
                    !_dragStartRotation.TryGetValue(obj.Id, out double startAngle) ||
                    !_dragStartSkew.TryGetValue(obj.Id, out (double X, double Y) startSkew))
                {
                    continue;
                }

                bool changed = start != obj.Bounds ||
                               Math.Abs(startAngle - obj.RotationDegrees) > 0.001 ||
                               Math.Abs(startSkew.X - obj.SkewXDegrees) > 0.001 ||
                               Math.Abs(startSkew.Y - obj.SkewYDegrees) > 0.001;
                if (!changed)
                {
                    continue;
                }

                // Put the object back where it started, then apply the end state through
                // the undo stack so the whole drag is one step.
                RectMm finalBounds = obj.Bounds;
                double finalAngle = obj.RotationDegrees;
                double finalSkewX = obj.SkewXDegrees, finalSkewY = obj.SkewYDegrees;
                obj.Bounds = start;
                obj.RotationDegrees = startAngle;
                obj.SkewXDegrees = startSkew.X;
                obj.SkewYDegrees = startSkew.Y;
                commands.Add(new TransformObjectCommand(obj, finalBounds, finalAngle, finalSkewX, finalSkewY));
            }

            if (commands.Count == 1)
            {
                _session.Execute(commands[0]);
            }
            else if (commands.Count > 1)
            {
                _session.Execute(new CompositeCommand("Move objects", commands));
            }
        }

        ResetDragState();
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    private void ResetDragState()
    {
        _dragging = _resizing = _rotating = _skewing = false;
        _activeHandle = Handle.None;
        _dragStartBounds.Clear();
        _dragStartRotation.Clear();
        _dragStartSkew.Clear();
        _groupResizeStart = null;
        _activeSnapLines = Array.Empty<SnapLine>();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _panning = _marquee = false;
        _guideDrag = null;
        if (_dragging || _resizing)
        {
            // Capture was taken away mid-drag (e.g. a dialog): undo the half-finished drag.
            RestoreDragStart();
        }

        ResetDragState();
        InvalidateVisual();
    }

    private void RestoreDragStart()
    {
        if (_session is null)
        {
            return;
        }

        foreach (LabelObject obj in _session.Selection)
        {
            if (_dragStartBounds.TryGetValue(obj.Id, out RectMm b))
            {
                obj.Bounds = b;
            }

            if (_dragStartRotation.TryGetValue(obj.Id, out double r))
            {
                obj.RotationDegrees = r;
            }

            if (_dragStartSkew.TryGetValue(obj.Id, out (double X, double Y) s))
            {
                obj.SkewXDegrees = s.X;
                obj.SkewYDegrees = s.Y;
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Space)
        {
            _spaceDown = true;
            Cursor = Cursors.SizeAll;
            e.Handled = true;
            return;
        }

        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case Key.D0 when ctrl: ZoomToActualSize(); e.Handled = true; return;
            case Key.D1 when ctrl: FitToView(); e.Handled = true; return;
            case Key.OemPlus or Key.Add: ZoomIn(); e.Handled = true; return;
            case Key.OemMinus or Key.Subtract: ZoomOut(); e.Handled = true; return;
        }

        if (_session is null)
        {
            return;
        }

        // Enter / F2 on a single selected text box edits it in place.
        if (e.Key is Key.Enter or Key.F2 && Keyboard.Modifiers == ModifierKeys.None &&
            _session.Selection is [TextObject text] && !text.Locked)
        {
            EditTextRequested?.Invoke(this, text);
            e.Handled = true;
            return;
        }

        double step = (Settings?.GridSizeMm ?? 1.0) is var gs && gs > 0 ? gs : 1.0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            step *= 10;
        }

        Vector delta = e.Key switch
        {
            Key.Left => new Vector(-step, 0),
            Key.Right => new Vector(step, 0),
            Key.Up => new Vector(0, -step),
            Key.Down => new Vector(0, step),
            _ => default,
        };

        if (delta == default)
        {
            return;
        }

        e.Handled = true;

        if (_session.Selection.Count == 0)
        {
            _pan -= delta * Scale;
            InvalidateVisual();
            return;
        }

        var commands = new List<IUndoableCommand>();
        foreach (LabelObject obj in _session.Selection.Where(o => !o.Locked))
        {
            commands.Add(new TransformObjectCommand(obj, obj.Bounds.Offset(delta.X, delta.Y), obj.RotationDegrees));
        }

        if (commands.Count > 0)
        {
            _session.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand("Nudge", commands));
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            _spaceDown = false;
            Cursor = null;
            e.Handled = true;
        }
    }

    // ---- snapping ----------------------------------------------------

    /// <summary>Snap tolerance in document mm, with a small floor so it stays usable at very high zoom.</summary>
    private double SnapToleranceMm() => Math.Max((Settings?.SnapPixels ?? 10.0) / Scale, 0.12);

    private (IReadOnlyList<double> V, IReadOnlyList<double> H) SnapTargets(LabelObject? exclude)
    {
        var v = new List<double>();
        var h = new List<double>();
        LabelDocument label = _session!.ActiveLabel;

        // label edges + centre
        v.Add(0);
        v.Add(label.SizeMm.Width / 2);
        v.Add(label.SizeMm.Width);
        h.Add(0);
        h.Add(label.SizeMm.Height / 2);
        h.Add(label.SizeMm.Height);

        if (Settings?.SnapToGuides != false)
        {
            v.AddRange(label.Guides.VerticalMm);
            h.AddRange(label.Guides.HorizontalMm);
        }

        if (Settings?.SnapToObjects != false)
        {
            foreach (LabelObject o in label.Objects)
            {
                if (ReferenceEquals(o, exclude) || _session.Selection.Contains(o) || !o.Visible)
                {
                    continue;
                }

                RectMm b = o.Bounds;
                v.Add(b.X);
                v.Add(b.Center.X);
                v.Add(b.Right);
                h.Add(b.Y);
                h.Add(b.Center.Y);
                h.Add(b.Bottom);
            }
        }

        return (v, h);
    }

    private IReadOnlyList<SnapLine> ApplyMoveSnap()
    {
        if (_session!.Selection.Count == 0)
        {
            return Array.Empty<SnapLine>();
        }

        double tolMm = SnapToleranceMm();
        (IReadOnlyList<double> v, IReadOnlyList<double> hh) = SnapTargets(_session.Selection[0]);

        // Union bounds of the selection, so the group snaps as one.
        RectMm union = _session.Selection[0].Bounds;
        foreach (LabelObject o in _session.Selection.Skip(1))
        {
            union = union.Union(o.Bounds);
        }

        SnapResult r = SnapCalculator.SnapMove(union, v, hh, tolMm);
        if (r.Snapped)
        {
            foreach (LabelObject o in _session.Selection.Where(x => !x.Locked))
            {
                o.Bounds = o.Bounds.Offset(r.Dx, r.Dy);
            }
        }
        else if (Settings is { SnapToGrid: true, GridSizeMm: > 0 } s && _session.Selection.Count == 1)
        {
            LabelObject only = _session.Selection[0];
            double g = s.GridSizeMm;
            only.Bounds = only.Bounds with
            {
                X = Math.Round(only.Bounds.X / g) * g,
                Y = Math.Round(only.Bounds.Y / g) * g,
            };
        }

        return r.Lines;
    }

    private IReadOnlyList<SnapLine> ApplyResizeSnap()
    {
        if (_session!.Selection.Count != 1 || _session.Selection[0].Locked)
        {
            return Array.Empty<SnapLine>();
        }

        LabelObject obj = _session.Selection[0];
        double tolMm = SnapToleranceMm();
        (IReadOnlyList<double> v, IReadOnlyList<double> hh) = SnapTargets(obj);
        RectMm b = obj.Bounds;
        var lines = new List<SnapLine>(2);

        bool left = _activeHandle is Handle.Left or Handle.TopLeft or Handle.BottomLeft;
        bool right = _activeHandle is Handle.Right or Handle.TopRight or Handle.BottomRight;
        bool top = _activeHandle is Handle.Top or Handle.TopLeft or Handle.TopRight;
        bool bottom = _activeHandle is Handle.Bottom or Handle.BottomLeft or Handle.BottomRight;

        double l = b.Left, t = b.Top, rr = b.Right, bb = b.Bottom;

        if (left)
        {
            l = SnapCalculator.SnapValue(l, v, tolMm, out bool s);
            if (s)
            {
                lines.Add(new SnapLine(true, l));
            }
        }

        if (right)
        {
            rr = SnapCalculator.SnapValue(rr, v, tolMm, out bool s);
            if (s)
            {
                lines.Add(new SnapLine(true, rr));
            }
        }

        if (top)
        {
            t = SnapCalculator.SnapValue(t, hh, tolMm, out bool s);
            if (s)
            {
                lines.Add(new SnapLine(false, t));
            }
        }

        if (bottom)
        {
            bb = SnapCalculator.SnapValue(bb, hh, tolMm, out bool s);
            if (s)
            {
                lines.Add(new SnapLine(false, bb));
            }
        }

        // Grid snapping applies to the edge being dragged too, not just to moves:
        // only the edges that actually moved are rounded, and only when no guide or
        // object edge already claimed them.
        if (Settings is { SnapToGrid: true, GridSizeMm: > 0 } grid && lines.Count == 0)
        {
            double g = grid.GridSizeMm;
            if (left)
            {
                l = Math.Round(l / g) * g;
            }

            if (right)
            {
                rr = Math.Round(rr / g) * g;
            }

            if (top)
            {
                t = Math.Round(t / g) * g;
            }

            if (bottom)
            {
                bb = Math.Round(bb / g) * g;
            }
        }

        if (rr - l >= 1 && bb - t >= 1)
        {
            obj.Bounds = RectMm.FromLtrb(l, t, rr, bb);
        }

        return lines;
    }

    // ---- hit testing ----------------------------------------------

    private bool TryHitGuide(Point screen, out GuideDrag drag)
    {
        drag = default;
        if (_session is null)
        {
            return false;
        }

        var guides = _session.ActiveLabel.Guides;
        for (int i = 0; i < guides.VerticalMm.Count; i++)
        {
            if (Math.Abs(MmToScreen(guides.VerticalMm[i], 0).X - screen.X) <= GuideHitPx && screen.Y > RulerPad)
            {
                drag = new GuideDrag(true, i, guides.VerticalMm[i]);
                return true;
            }
        }

        for (int i = 0; i < guides.HorizontalMm.Count; i++)
        {
            if (Math.Abs(MmToScreen(0, guides.HorizontalMm[i]).Y - screen.Y) <= GuideHitPx && screen.X > RulerPad)
            {
                drag = new GuideDrag(false, i, guides.HorizontalMm[i]);
                return true;
            }
        }

        return false;
    }

    private void BeginPan(Point screen)
    {
        _panning = true;
        _panStartScreen = screen;
        _panStartValue = _pan;
        Cursor = Cursors.SizeAll;
        CaptureMouse();
    }

    private void UpdateCursor(Point screen)
    {
        if (_panning || _spaceDown)
        {
            Cursor = Cursors.SizeAll;
            return;
        }

        if (_session is null)
        {
            Cursor = Cursors.Arrow;
            return;
        }

        Point mm = ScreenToMm(screen);
        if (SelectedGroupBounds() is { } group && HitRectHandle(group, screen) is var gh and not Handle.None)
        {
            Cursor = gh switch
            {
                Handle.Top or Handle.Bottom => Cursors.SizeNS,
                Handle.Left or Handle.Right => Cursors.SizeWE,
                Handle.TopLeft or Handle.BottomRight => Cursors.SizeNWSE,
                _ => Cursors.SizeNESW,
            };
            return;
        }

        foreach (LabelObject obj in SelectionWithHandles())
        {
            if (!obj.Locked && HitHandle(obj, screen) is var h and not Handle.None)
            {
                Cursor = HandleCursor(obj, h);
                return;
            }
        }

        // Match OnMouseDown's priority: an object under the cursor wins over a
        // coincident guide line (e.g. right after the object snapped to it).
        if (HitObject(mm) is not null)
        {
            Cursor = Cursors.SizeAll;
            return;
        }

        if (Settings?.ShowGuides != false && !_session.ActiveLabel.Guides.Locked && TryHitGuide(screen, out GuideDrag g))
        {
            Cursor = g.Vertical ? Cursors.SizeWE : Cursors.SizeNS;
            return;
        }

        Cursor = Cursors.Arrow;
    }

    private void CaptureStart()
    {
        _dragStartBounds = _session!.Selection.ToDictionary(o => o.Id, o => o.Bounds);
        _dragStartRotation = _session.Selection.ToDictionary(o => o.Id, o => o.RotationDegrees);
        _dragStartSkew = _session.Selection.ToDictionary(o => o.Id, o => (o.SkewXDegrees, o.SkewYDegrees));
    }

    private LabelObject? HitObject(Point mm)
    {
        if (_session is null)
        {
            return null;
        }

        var point = new PointMm(mm.X, mm.Y);
        double tol = 6 / Scale;
        for (int i = _session.ActiveLabel.Objects.Count - 1; i >= 0; i--)
        {
            LabelObject obj = _session.ActiveLabel.Objects[i];

            // The real (rotated, skewed) shape, not its bounding box: clicking the empty
            // corner beside a diamond-rotated barcode should reach what is underneath.
            if (obj.Visible && ObjectGeometry.Contains(obj, point, tol))
            {
                return obj;
            }
        }

        return null;
    }

    /// <summary>
    /// Handle under a screen point, found where the handles are actually drawn —
    /// on the transformed shape. (Testing against the upright box meant the handles
    /// of a rotated object could not be grabbed where they were shown.)
    /// </summary>
    private Handle HitHandle(LabelObject obj, Point screen)
    {
        double tol = (HandleSize / 2.0) + 4;
        Handle best = Handle.None;
        double bestDistance = double.MaxValue;

        foreach ((Handle handle, PointMm local) in HandleLocals(obj))
        {
            PointMm world = ObjectGeometry.ToWorld(obj, local);
            Point p = MmToScreen(world.X, world.Y);
            double d = Math.Max(Math.Abs(p.X - screen.X), Math.Abs(p.Y - screen.Y));
            if (d <= tol && d < bestDistance)
            {
                best = handle;
                bestDistance = d;
            }
        }

        return best;
    }

    /// <summary>A resize cursor pointing along the handle's real on-screen direction.</summary>
    private Cursor HandleCursor(LabelObject obj, Handle handle)
    {
        if (handle == Handle.Rotate)
        {
            return Cursors.Hand;
        }

        bool side = handle is Handle.Top or Handle.Bottom or Handle.Left or Handle.Right;
        if (side && (_freeTransform || (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))))
        {
            // Skewing a top/bottom edge slides it sideways, and vice versa.
            return handle is Handle.Top or Handle.Bottom ? Cursors.SizeWE : Cursors.SizeNS;
        }

        PointMm local = HandleLocals(obj).First(h => h.Handle == handle).Local;
        PointMm dir = ObjectGeometry.Linear(obj).ApplyVector(local.X, local.Y);
        double angle = ((Math.Atan2(dir.Y, dir.X) * 180.0 / Math.PI) + 360) % 180;

        return angle switch
        {
            < 22.5 or >= 157.5 => Cursors.SizeWE,
            < 67.5 => Cursors.SizeNWSE,
            < 112.5 => Cursors.SizeNS,
            _ => Cursors.SizeNESW,
        };
    }

    /// <summary>
    /// Handle drag maths. <paramref name="proportional"/> (Shift) keeps the original
    /// aspect ratio; <paramref name="fromCentre"/> (Ctrl) grows or shrinks about the
    /// object's centre instead of the opposite edge. Holding both does both.
    /// </summary>
    private static RectMm ResizeRect(
        RectMm start, Handle handle, double dx, double dy, bool proportional = false, bool fromCentre = false)
    {
        if (proportional && start.Width > 0 && start.Height > 0 &&
            handle is Handle.TopLeft or Handle.TopRight or Handle.BottomLeft or Handle.BottomRight)
        {
            // Drive both axes from whichever the pointer moved further on, so the
            // corner tracks the cursor without the box drifting off the diagonal.
            double aspect = start.Width / start.Height;
            bool left = handle is Handle.TopLeft or Handle.BottomLeft;
            bool top = handle is Handle.TopLeft or Handle.TopRight;

            double widthDelta = left ? -dx : dx;
            double heightDelta = top ? -dy : dy;

            if (Math.Abs(widthDelta) * (1 / aspect) >= Math.Abs(heightDelta))
            {
                heightDelta = widthDelta / aspect;
            }
            else
            {
                widthDelta = heightDelta * aspect;
            }

            dx = left ? -widthDelta : widthDelta;
            dy = top ? -heightDelta : heightDelta;
        }

        double l = start.Left, t = start.Top, r = start.Right, b = start.Bottom;
        switch (handle)
        {
            case Handle.TopLeft: l += dx; t += dy; break;
            case Handle.Top: t += dy; break;
            case Handle.TopRight: r += dx; t += dy; break;
            case Handle.Right: r += dx; break;
            case Handle.BottomRight: r += dx; b += dy; break;
            case Handle.Bottom: b += dy; break;
            case Handle.BottomLeft: l += dx; b += dy; break;
            case Handle.Left: l += dx; break;
        }

        if (fromCentre)
        {
            // Mirror the same movement onto the opposite edge.
            switch (handle)
            {
                case Handle.TopLeft: r -= dx; b -= dy; break;
                case Handle.Top: b -= dy; break;
                case Handle.TopRight: l -= dx; b -= dy; break;
                case Handle.Right: l -= dx; break;
                case Handle.BottomRight: l -= dx; t -= dy; break;
                case Handle.Bottom: t -= dy; break;
                case Handle.BottomLeft: r -= dx; t -= dy; break;
                case Handle.Left: r -= dx; break;
            }
        }

        const double min = 1.0;
        if (r - l < min)
        {
            if (handle is Handle.Left or Handle.TopLeft or Handle.BottomLeft)
            {
                l = r - min;
            }
            else
            {
                r = l + min;
            }
        }

        if (b - t < min)
        {
            if (handle is Handle.Top or Handle.TopLeft or Handle.TopRight)
            {
                t = b - min;
            }
            else
            {
                b = t + min;
            }
        }

        return RectMm.FromLtrb(l, t, r, b);
    }

    private static LabelMaker.Core.Formtec.LabelTemplate Fallback() => new()
    {
        Id = "adhoc",
        Name = "Ad-hoc",
        Grid = new LabelMaker.Core.Formtec.LabelGrid { Columns = 1, Rows = 1, LabelWidthMm = 100, LabelHeightMm = 50 },
    };

    private readonly record struct GuideDrag(bool Vertical, int Index, double ValueMm);

    private enum Handle
    {
        None,
        TopLeft,
        Top,
        TopRight,
        Right,
        BottomRight,
        Bottom,
        BottomLeft,
        Left,

        /// <summary>The knob above the top edge.</summary>
        Rotate,
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
