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
/// then overlays selection handles, grid and guides.
///
/// Navigation (spec §2):
///   • mouse wheel            — zoom toward the cursor
///   • middle-drag            — pan
///   • Space + left-drag      — pan
///   • left-drag on empty     — pan
///   • Ctrl+0 / Ctrl+1        — actual size / fit
///   • arrow keys             — nudge selection (Shift = ×10), or pan when nothing selected
/// <c>_zoom</c> is a true scale factor: 1.0 == 100 % (physical size at a 96-DPI
/// baseline); the "Actual size" command sets it from the calibrated screen DPI.
/// </summary>
public sealed class DesignerCanvas : FrameworkElement
{
    private const double K = UnitConverter.DiuPerInch / UnitConverter.MmPerInch; // DIU per mm
    private const double HandleSize = 8.0;
    private const double MinZoom = 0.05;
    private const double MaxZoom = 60.0;

    private readonly SheetVisualRenderer _renderer;
    private DocumentSession? _session;

    private double _zoom = 1.0;
    private Vector _pan;                 // extra translation in DIU, on top of centring
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

    public DesignerCanvas(SheetVisualRenderer renderer)
    {
        _renderer = renderer;
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    public event EventHandler? ZoomChanged;

    public AppSettings? Settings { get; set; }

    /// <summary>Current scale factor (1.0 == 100 %).</summary>
    public double Zoom => _zoom;

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

    private void OnSessionChanged(object? sender, EventArgs e) => InvalidateVisual();

    // ---- view transform -------------------------------------------------------

    private double LabelWidthMm => _session?.ActiveLabel.SizeMm.Width ?? 100;
    private double LabelHeightMm => _session?.ActiveLabel.SizeMm.Height ?? 60;

    /// <summary>DIU per document millimetre at the current zoom.</summary>
    private double Scale => _zoom * K;

    /// <summary>Control-space position of the label's top-left corner.</summary>
    private Point ContentOrigin => new(
        ((ActualWidth - (LabelWidthMm * Scale)) / 2.0) + _pan.X,
        ((ActualHeight - (LabelHeightMm * Scale)) / 2.0) + _pan.Y);

    private Point ScreenToMm(Point p)
    {
        Point o = ContentOrigin;
        return new Point((p.X - o.X) / Scale, (p.Y - o.Y) / Scale);
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

        const double margin = 56;
        double sx = (ActualWidth - (margin * 2)) / (LabelWidthMm * K);
        double sy = (ActualHeight - (margin * 2)) / (LabelHeightMm * K);
        _zoom = Math.Clamp(Math.Min(sx, sy), MinZoom, MaxZoom);
        _pan = default;
        _viewInitialised = true;
        InvalidateVisual();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ZoomToActualSize()
    {
        double screenDpi = Settings?.ScreenDpiCalibration ?? UnitConverter.DiuPerInch;
        SetZoomKeepingCentre(Math.Clamp(screenDpi / UnitConverter.DiuPerInch, MinZoom, MaxZoom));
    }

    public void ZoomIn() => ZoomAt(1.2, Centre);

    public void ZoomOut() => ZoomAt(1 / 1.2, Centre);

    private Point Centre => new(ActualWidth / 2.0, ActualHeight / 2.0);

    private void SetZoomKeepingCentre(double newZoom) => ZoomToward(newZoom, Centre);

    private void ZoomAt(double factor, Point anchorScreen) =>
        ZoomToward(Math.Clamp(_zoom * factor, MinZoom, MaxZoom), anchorScreen);

    private void ZoomToward(double newZoom, Point anchorScreen)
    {
        if (Math.Abs(newZoom - _zoom) < 1e-6)
        {
            return;
        }

        Point mmUnderCursor = ScreenToMm(anchorScreen);
        _zoom = newZoom;

        // Solve for _pan so the same document point stays under the anchor:
        //   anchor = origin + mm * Scale
        //   origin = (Avail - Label*Scale)/2 + pan
        _pan = new Vector(
            anchorScreen.X - (mmUnderCursor.X * Scale) - ((ActualWidth - (LabelWidthMm * Scale)) / 2.0),
            anchorScreen.Y - (mmUnderCursor.Y * Scale) - ((ActualHeight - (LabelHeightMm * Scale)) / 2.0));

        _viewInitialised = true;
        InvalidateVisual();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- rendering ----------------------------------------------------------

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2B, 0x2E, 0x33)), null, new Rect(RenderSize));
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

        // soft drop shadow
        double s = 6 / _zoom;
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), null,
            new Rect(paperRect.X + s, paperRect.Y + s, paperRect.Width, paperRect.Height));

        dc.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(0xC9, 0xCD, 0xD3)), 1.0 / _zoom), paperRect);
        dc.DrawDrawing(visual.Drawing);

        if (Settings?.ShowGrid == true)
        {
            DrawGrid(dc, label, Settings.GridSizeMm);
        }

        DrawSelection(dc);

        dc.Pop();
        dc.Pop();
    }

    private void DrawGrid(DrawingContext dc, LabelDocument label, double stepMm)
    {
        if (stepMm <= 0 || stepMm * Scale < 4)
        {
            return; // don't draw an unreadably dense grid
        }

        var pen = new Pen(new SolidColorBrush(Color.FromArgb(36, 47, 111, 235)), 0.6 / _zoom);
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

        var accent = Color.FromRgb(0x2F, 0x6F, 0xEB);
        var stroke = new Pen(new SolidColorBrush(accent), 1.2 / _zoom);
        stroke.Freeze();
        var handlePen = new Pen(new SolidColorBrush(accent), 1.2 / _zoom);
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

    // ---- interaction ------------------------------------------------------

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        double factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
        ZoomAt(factor, e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        if (_session is null)
        {
            return;
        }

        bool wantsPan = e.ChangedButton == MouseButton.Middle ||
                        (e.ChangedButton == MouseButton.Left && _spaceDown);

        if (wantsPan)
        {
            BeginPan(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        Point mm = ScreenToMm(e.GetPosition(this));
        _dragStartMm = mm;

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

        LabelObject? hit = HitObject(mm);
        if (hit is null)
        {
            _session.ClearSelection();
            BeginPan(e.GetPosition(this)); // drag empty space = pan
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

        if (_panning)
        {
            Point now = e.GetPosition(this);
            _pan = _panStartValue + (now - _panStartScreen);
            InvalidateVisual();
            return;
        }

        UpdateCursor(e.GetPosition(this));

        if (!_dragging && !_resizing)
        {
            return;
        }

        Point mm = ScreenToMm(e.GetPosition(this));
        double dx = mm.X - _dragStartMm.X;
        double dy = mm.Y - _dragStartMm.Y;

        foreach (LabelObject obj in _session.Selection)
        {
            if (obj.Locked || !_dragStartBounds.TryGetValue(obj.Id, out RectMm start))
            {
                continue;
            }

            obj.Bounds = _resizing ? ResizeRect(start, _activeHandle, dx, dy) : start.Offset(dx, dy);
        }

        if (Settings is { SnapToGrid: true, GridSizeMm: > 0 } s && !_resizing && _session.Selection.Count == 1)
        {
            LabelObject only = _session.Selection[0];
            double g = s.GridSizeMm;
            only.Bounds = only.Bounds with
            {
                X = Math.Round(only.Bounds.X / g) * g,
                Y = Math.Round(only.Bounds.Y / g) * g,
            };
        }

        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_panning)
        {
            _panning = false;
            ReleaseMouseCapture();
            UpdateCursor(e.GetPosition(this));
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
                    obj.Bounds = start; // command.Do() re-applies
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
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _panning = _dragging = _resizing = false;
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
            case Key.D0 when ctrl:
                ZoomToActualSize();
                e.Handled = true;
                return;
            case Key.D1 when ctrl:
                FitToView();
                e.Handled = true;
                return;
            case Key.OemPlus or Key.Add:
                ZoomIn();
                e.Handled = true;
                return;
            case Key.OemMinus or Key.Subtract:
                ZoomOut();
                e.Handled = true;
                return;
        }

        if (_session is null)
        {
            return;
        }

        double step = (Settings?.GridSizeMm ?? 1.0) is var g && g > 0 ? g : 1.0;
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
            _pan -= delta * Scale;   // pan the view
            InvalidateVisual();
            return;
        }

        var commands = new List<IUndoableCommand>();
        foreach (LabelObject obj in _session.Selection.Where(o => !o.Locked))
        {
            RectMm target = obj.Bounds.Offset(delta.X, delta.Y);
            commands.Add(new TransformObjectCommand(obj, target, obj.RotationDegrees));
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

        if (_session is not null)
        {
            foreach (LabelObject obj in _session.Selection)
            {
                if (!obj.Locked && HitHandle(obj, ScreenToMm(screen)) is var h and not Handle.None)
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

            Cursor = HitObject(ScreenToMm(screen)) is not null ? Cursors.SizeAll : Cursors.Arrow;
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
