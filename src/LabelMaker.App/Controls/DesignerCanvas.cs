using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LabelMaker.App.Rendering;
using LabelMaker.App.Services;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
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

    private static readonly Color Accent = Color.FromRgb(0x5A, 0x4F, 0xE0);
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
    private Handle _activeHandle = Handle.None;
    private Point _dragStartMm;
    private Dictionary<string, RectMm> _dragStartBounds = new();
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

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xE9, 0xEB, 0xF5)), null, new Rect(RenderSize));
        if (_session is null)
        {
            return;
        }

        LabelDocument label = _session.ActiveLabel;
        var visual = _renderer.RenderSingleLabel(
            label,
            _session.CurrentResolveContext(),
            _renderer.OptionsFor(_session.Project.Template ?? Fallback(), LabelMaker.Core.Rendering.LabelOutlineStyle.Hairline),
            ColorRgba.White);

        Point origin = ContentOrigin;
        dc.PushTransform(new TranslateTransform(origin.X, origin.Y));
        dc.PushTransform(new ScaleTransform(_zoom, _zoom));

        var paperRect = new Rect(0, 0, label.SizeMm.Width * K, label.SizeMm.Height * K);
        double sh = 10 / _zoom;
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(28, 40, 30, 90)), null,
            new Rect(paperRect.X + sh, paperRect.Y + sh, paperRect.Width, paperRect.Height));
        dc.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(0xCE, 0xD1, 0xDE)), 1.0 / _zoom), paperRect);
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

        var pen = new Pen(new SolidColorBrush(Color.FromArgb(30, 90, 79, 224)), 0.6 / _zoom);
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
        stroke.Freeze();
        var handlePen = new Pen(new SolidColorBrush(Accent), 1.3 / _zoom);
        handlePen.Freeze();

        foreach (LabelObject obj in _session.Selection)
        {
            RectMm b = obj.Bounds;
            var rect = new Rect(b.X * K, b.Y * K, b.Width * K, b.Height * K);
            dc.PushTransform(new RotateTransform(obj.RotationDegrees, rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2)));
            dc.DrawRectangle(null, stroke, rect);

            if (!obj.Locked)
            {
                double hs = HandleSize / _zoom;
                foreach (Point p in HandlePoints(rect))
                {
                    dc.DrawRectangle(Brushes.White, handlePen, new Rect(p.X - (hs / 2), p.Y - (hs / 2), hs, hs));
                }
            }

            dc.Pop();
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
        var bg = new SolidColorBrush(Color.FromRgb(0xF6, 0xF7, 0xFB));
        var line = new Pen(new SolidColorBrush(Color.FromRgb(0xD6, 0xD9, 0xE4)), 1);
        var tick = new Pen(new SolidColorBrush(Color.FromRgb(0x9A, 0x9F, 0xB5)), 1);
        var textBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x70, 0x88));

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
            var ft = Label(mm.ToString("0", CultureInfo.InvariantCulture), textBrush);
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
            var ft = Label(mm.ToString("0", CultureInfo.InvariantCulture), textBrush);
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

    private static IEnumerable<Point> HandlePoints(Rect r)
    {
        yield return new Point(r.Left, r.Top);
        yield return new Point(r.Left + (r.Width / 2), r.Top);
        yield return new Point(r.Right, r.Top);
        yield return new Point(r.Right, r.Top + (r.Height / 2));
        yield return new Point(r.Right, r.Bottom);
        yield return new Point(r.Left + (r.Width / 2), r.Bottom);
        yield return new Point(r.Left, r.Bottom);
        yield return new Point(r.Left, r.Top + (r.Height / 2));
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

        // Resize handles on the current selection take priority over everything else.
        foreach (LabelObject obj in _session.Selection)
        {
            Handle h = obj.Locked ? Handle.None : HitHandle(obj, mm);
            if (h != Handle.None)
            {
                _resizing = true;
                _activeHandle = h;
                CaptureStart();
                CaptureMouse();
                e.Handled = true;
                return;
            }
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
            _session.ClearSelection();
            BeginPan(screen);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            var sel = _session.Selection.ToList();
            if (!sel.Remove(hit))
            {
                sel.Add(hit);
            }

            _session.SetSelection(sel);
        }
        else if (!_session.Selection.Contains(hit))
        {
            _session.SetSelection(new[] { hit });
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

        foreach (LabelObject obj in _session.Selection)
        {
            if (obj.Locked || !_dragStartBounds.TryGetValue(obj.Id, out RectMm start))
            {
                continue;
            }

            obj.Bounds = _resizing ? ResizeRect(start, _activeHandle, dx, dy) : start.Offset(dx, dy);
        }

        _activeSnapLines = _resizing ? ApplyResizeSnap() : ApplyMoveSnap();

        InvalidateVisual();
    }

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

        if (_session is not null && (_dragging || _resizing))
        {
            var commands = new List<IUndoableCommand>();
            foreach (LabelObject obj in _session.Selection)
            {
                if (_dragStartBounds.TryGetValue(obj.Id, out RectMm start) && start != obj.Bounds)
                {
                    RectMm final = obj.Bounds;
                    obj.Bounds = start;
                    commands.Add(new TransformObjectCommand(obj, final, obj.RotationDegrees));
                }
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

        _dragging = _resizing = false;
        _activeHandle = Handle.None;
        _dragStartBounds.Clear();
        _activeSnapLines = Array.Empty<SnapLine>();
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _panning = _dragging = _resizing = false;
        _guideDrag = null;
        _activeSnapLines = Array.Empty<SnapLine>();
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
        foreach (LabelObject obj in _session.Selection)
        {
            if (!obj.Locked && HitHandle(obj, mm) is var h and not Handle.None)
            {
                Cursor = h switch
                {
                    Handle.Top or Handle.Bottom => Cursors.SizeNS,
                    Handle.Left or Handle.Right => Cursors.SizeWE,
                    Handle.TopLeft or Handle.BottomRight => Cursors.SizeNWSE,
                    _ => Cursors.SizeNESW,
                };
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

    private void CaptureStart() => _dragStartBounds = _session!.Selection.ToDictionary(o => o.Id, o => o.Bounds);

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
            if (obj.Visible && obj.RotatedBounds.Inflate(tol, tol).Contains(point))
            {
                return obj;
            }
        }

        return null;
    }

    private Handle HitHandle(LabelObject obj, Point mm)
    {
        RectMm b = obj.Bounds;
        var rect = new Rect(b.X * K, b.Y * K, b.Width * K, b.Height * K);
        var p = new Point(mm.X * K, mm.Y * K);
        double tol = (HandleSize + 4) / _zoom;

        Handle[] order = { Handle.TopLeft, Handle.Top, Handle.TopRight, Handle.Right, Handle.BottomRight, Handle.Bottom, Handle.BottomLeft, Handle.Left };
        int idx = 0;
        foreach (Point hp in HandlePoints(rect))
        {
            if (Math.Abs(hp.X - p.X) <= tol && Math.Abs(hp.Y - p.Y) <= tol)
            {
                return order[idx];
            }

            idx++;
        }

        return Handle.None;
    }

    private static RectMm ResizeRect(RectMm start, Handle handle, double dx, double dy)
    {
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
