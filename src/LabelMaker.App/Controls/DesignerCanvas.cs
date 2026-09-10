using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LabelMaker.App.Rendering;
using LabelMaker.App.Services;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Settings;
using LabelMaker.Core.Model;
using LabelMaker.Core.Units;

namespace LabelMaker.App.Controls;

/// <summary>
/// The WYSIWYG design surface. Renders the active label through the shared
/// <see cref="LabelMaker.Core.Rendering.LabelRenderer"/> (so it matches print),
/// then overlays selection handles, grid and snap guides. Supports click-select,
/// marquee-free multi-select with Shift, drag-move, 8-handle resize, wheel zoom
/// and keyboard nudge (spec §2, §21).
/// </summary>
public sealed class DesignerCanvas : FrameworkElement
{
    private const double K = UnitConverter.DiuPerInch / UnitConverter.MmPerInch;
    private const double HandleSize = 7.0;
    private const double OriginMarginDiu = 40.0;

    private readonly SheetVisualRenderer _renderer;
    private DocumentSession? _session;

    private double _zoom = 3.0;
    private Point _originDiu = new(OriginMarginDiu, OriginMarginDiu);

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
    }

    public event EventHandler? ZoomChanged;

    public double Zoom
    {
        get => _zoom;
        set
        {
            _zoom = Math.Clamp(value, 0.5, 20.0);
            InvalidateVisual();
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public AppSettings? Settings { get; set; }

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
        InvalidateVisual();
    }

    private void OnSessionChanged(object? sender, EventArgs e) => InvalidateVisual();

    // ---- rendering ----

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x3A, 0x3D, 0x41)), null, new Rect(RenderSize));
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

        dc.PushTransform(new TranslateTransform(_originDiu.X, _originDiu.Y));
        dc.PushTransform(new ScaleTransform(_zoom, _zoom));

        // Drop shadow / paper edge.
        var paperRect = new Rect(0, 0, label.SizeMm.Width * K, label.SizeMm.Height * K);
        dc.DrawRectangle(Brushes.White, new Pen(Brushes.Black, 0.3 / _zoom), paperRect);
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
        if (stepMm <= 0)
        {
            return;
        }

        var pen = new Pen(new SolidColorBrush(Color.FromArgb(40, 0, 120, 215)), 0.2 / _zoom);
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

        var stroke = new Pen(new SolidColorBrush(Color.FromRgb(0, 120, 215)), 1.0 / _zoom);
        stroke.Freeze();
        var handleFill = Brushes.White;
        var handlePen = new Pen(new SolidColorBrush(Color.FromRgb(0, 120, 215)), 1.0 / _zoom);
        handlePen.Freeze();

        foreach (LabelObject obj in _session.Selection)
        {
            RectMm b = obj.Bounds;
            var rect = new Rect(b.X * K, b.Y * K, b.Width * K, b.Height * K);

            dc.PushTransform(new RotateTransform(obj.RotationDegrees, rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2)));
            dc.DrawRectangle(null, stroke, rect);

            double hs = HandleSize / _zoom;
            foreach (Point p in HandlePoints(rect))
            {
                dc.DrawRectangle(handleFill, handlePen, new Rect(p.X - (hs / 2), p.Y - (hs / 2), hs, hs));
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

    // ---- interaction ----

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Point before = ScreenToMm(e.GetPosition(this));
            Zoom *= e.Delta > 0 ? 1.1 : 1 / 1.1;
            Point after = ScreenToMm(e.GetPosition(this));
            _originDiu.X += (after.X - before.X) * K * _zoom;
            _originDiu.Y += (after.Y - before.Y) * K * _zoom;
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        if (_session is null)
        {
            return;
        }

        Point mm = ScreenToMm(e.GetPosition(this));
        _dragStartMm = mm;

        // Resize handle hit-test first, on the current selection.
        foreach (LabelObject obj in _session.Selection)
        {
            Handle h = HitHandle(obj, mm);
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
        if (_session is null || (!_dragging && !_resizing))
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

        if (Settings?.SnapToGrid == true && !_resizing && _session.Selection.Count == 1)
        {
            LabelObject only = _session.Selection[0];
            double g = Settings.GridSizeMm;
            if (g > 0)
            {
                only.Bounds = only.Bounds with
                {
                    X = Math.Round(only.Bounds.X / g) * g,
                    Y = Math.Round(only.Bounds.Y / g) * g,
                };
            }
        }

        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
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

    private void CaptureStart()
    {
        _dragStartBounds = _session!.Selection.ToDictionary(o => o.Id, o => o.Bounds);
    }

    private LabelObject? HitObject(Point mm)
    {
        if (_session is null)
        {
            return null;
        }

        var point = new PointMm(mm.X, mm.Y);
        for (int i = _session.ActiveLabel.Objects.Count - 1; i >= 0; i--)
        {
            LabelObject obj = _session.ActiveLabel.Objects[i];
            if (obj.Visible && obj.RotatedBounds.Inflate(0.5, 0.5).Contains(point))
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
        double tol = (HandleSize + 3) / _zoom;

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

        double minSize = 1.0;
        if (r - l < minSize)
        {
            if (handle is Handle.Left or Handle.TopLeft or Handle.BottomLeft)
            {
                l = r - minSize;
            }
            else
            {
                r = l + minSize;
            }
        }

        if (b - t < minSize)
        {
            if (handle is Handle.Top or Handle.TopLeft or Handle.TopRight)
            {
                t = b - minSize;
            }
            else
            {
                b = t + minSize;
            }
        }

        return RectMm.FromLtrb(l, t, r, b);
    }

    private Point ScreenToMm(Point screen) => new(
        (screen.X - _originDiu.X) / (_zoom * K),
        (screen.Y - _originDiu.Y) / (_zoom * K));

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
