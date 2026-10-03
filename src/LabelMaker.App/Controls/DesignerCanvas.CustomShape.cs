using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Units;

namespace LabelMaker.App.Controls;

/// <summary>
/// The custom shape tool: click dots on the label and they are joined into a shape. Click the
/// first dot again (or press Enter, or double-click) to close it; Backspace takes the last dot
/// back and Esc gives up. Shift keeps each new line level, upright or at 45°. Double-clicking a
/// finished custom shape lets its dots be dragged, added (double-click a line) and removed
/// (Delete on a dot).
/// </summary>
public sealed partial class DesignerCanvas
{
    private const double DotRadiusPx = 4.5;
    private const double DotHitPx = 9;

    // Drawing a new shape.
    private bool _drawingShape;
    private readonly List<PointMm> _newDots = new();
    private PointMm? _hoverDot;

    // Editing the dots of an existing shape.
    private ShapeObject? _editShape;
    private List<PointMm> _editDots = new();
    private (RectMm Bounds, List<PointMm> Points) _editBefore;
    private int _dragDot = -1;
    private int _activeDot = -1;

    /// <summary>Raised when the custom shape tool starts or stops, so the window can say what to do.</summary>
    public event EventHandler? PointToolChanged;

    /// <summary>True while dots are being placed or a custom shape's dots are being edited.</summary>
    public bool IsPointToolActive => _drawingShape || _editShape is not null;

    /// <summary>What to tell the user while the tool is on.</summary>
    public string PointToolHint => _drawingShape
        ? _newDots.Count < 3
            ? "Custom shape: click on the label to place dots. Shift keeps lines straight. Esc cancels."
            : "Custom shape: keep clicking to add dots. Click the first dot, double-click or press Enter to finish. Backspace removes the last dot."
        : "Editing dots: drag a dot to move it, double-click a line to add one, Delete removes the chosen dot. Enter or click outside to finish, Esc to undo.";

    /// <summary>Starts placing dots for a new custom shape.</summary>
    public void BeginCustomShape()
    {
        if (_session is null)
        {
            return;
        }

        EndPointEdit(commit: true);
        _drawingShape = true;
        _newDots.Clear();
        _hoverDot = null;
        _session.ClearSelection();
        Cursor = Cursors.Cross;
        Focus();
        PointToolChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>Lets the dots of <paramref name="shape"/> be dragged, added and removed.</summary>
    public void BeginPointEdit(ShapeObject shape)
    {
        if (_session is null || shape.Shape != ShapeKind.Custom || shape.Locked)
        {
            return;
        }

        CancelCustomShape();
        _editShape = shape;
        _editDots = CustomShape.WorldPoints(shape);
        _editBefore = (shape.Bounds, new List<PointMm>(shape.CustomPoints));
        _dragDot = _activeDot = -1;
        _session.SetSelection(new[] { shape });
        Focus();
        PointToolChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void CancelCustomShape()
    {
        if (!_drawingShape)
        {
            return;
        }

        _drawingShape = false;
        _newDots.Clear();
        _hoverDot = null;
        Cursor = null;
        PointToolChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void FinishCustomShape()
    {
        if (_session is null)
        {
            return;
        }

        List<PointMm> dots = _newDots.ToList();
        bool closed = dots.Count >= 3;
        CancelCustomShape();
        if (dots.Count < 2)
        {
            return;
        }

        ShapeObject shape = CustomShape.Create(dots, closed);
        shape.Name = "Custom shape";
        _session.Execute(new AddObjectCommand(_session.ActiveLabel, shape));
        _session.SetSelection(new[] { shape });
    }

    /// <summary>Ends dot editing; on <paramref name="commit"/> the change becomes one undo step.</summary>
    private void EndPointEdit(bool commit)
    {
        if (_editShape is not { } shape || _session is null)
        {
            return;
        }

        // Undone, deleted or on another page since editing began: nothing to commit.
        if (!_session.ActiveLabel.Objects.Contains(shape))
        {
            _editShape = null;
            _dragDot = _activeDot = -1;
            PointToolChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            return;
        }

        (RectMm bounds, List<PointMm> points) = (shape.Bounds, shape.CustomPoints);
        shape.Bounds = _editBefore.Bounds;
        shape.CustomPoints = _editBefore.Points;
        _editShape = null;
        _dragDot = _activeDot = -1;

        bool changed = bounds != _editBefore.Bounds || !points.SequenceEqual(_editBefore.Points);
        if (commit && changed)
        {
            _session.Execute(new CompositeCommand("Edit shape dots", new IUndoableCommand[]
            {
                new SetPropertyCommand<RectMm>("Edit shape dots", () => shape.Bounds, v => shape.Bounds = v, bounds),
                new SetPropertyCommand<List<PointMm>>("Edit shape dots", () => shape.CustomPoints, v => shape.CustomPoints = v, points),
            }));
        }

        PointToolChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>Applies <see cref="_editDots"/> to the shape being edited (live, not yet undoable).</summary>
    private void ApplyEditDots()
    {
        if (_editShape is not { } shape)
        {
            return;
        }

        (RectMm bounds, List<PointMm> points) = CustomShape.Refit(shape, _editDots);
        shape.Bounds = bounds;
        shape.CustomPoints = points;
    }

    // ---- input -------------------------------------------------------------------------

    /// <summary>A dot where the pointer is: on the grid when snapping, straight from the last dot with Shift.</summary>
    private PointMm DotAt(Point screen, PointMm? from)
    {
        Point mm = ScreenToMm(screen);
        double x = mm.X, y = mm.Y;
        if (from is { } a && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            double dx = x - a.X, dy = y - a.Y;
            double angle = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
            double length = Math.Sqrt((dx * dx) + (dy * dy));
            x = a.X + (Math.Cos(angle) * length);
            y = a.Y + (Math.Sin(angle) * length);
        }
        else if (GridStepMm is var grid and > 0)
        {
            x = Math.Round(x / grid) * grid;
            y = Math.Round(y / grid) * grid;
        }

        return new PointMm(Math.Round(x, 3), Math.Round(y, 3));
    }

    private int DotNear(IReadOnlyList<PointMm> dots, Point screen)
    {
        for (int i = dots.Count - 1; i >= 0; i--)
        {
            Point p = MmToScreen(dots[i].X, dots[i].Y);
            if ((p - screen).Length <= DotHitPx)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Handles a left click while the tool is on; false lets the normal canvas handle it.</summary>
    private bool PointToolMouseDown(MouseButtonEventArgs e, Point screen)
    {
        if (_drawingShape)
        {
            if (e.ClickCount == 2)
            {
                // The first click of the double-click already placed a dot.
                FinishCustomShape();
            }
            else if (_newDots.Count >= 3 && DotNear(new[] { _newDots[0] }, screen) == 0)
            {
                FinishCustomShape();
            }
            else
            {
                PointMm dot = DotAt(screen, _newDots.Count > 0 ? _newDots[^1] : null);
                if (_newDots.Count == 0 || dot != _newDots[^1])
                {
                    _newDots.Add(dot);
                    PointToolChanged?.Invoke(this, EventArgs.Empty);
                }
            }

            e.Handled = true;
            InvalidateVisual();
            return true;
        }

        if (_editShape is { } shape)
        {
            int hit = DotNear(_editDots, screen);
            if (hit >= 0)
            {
                _dragDot = _activeDot = hit;
                CaptureMouse();
            }
            else if (e.ClickCount == 2 && InsertDotOnEdge(screen))
            {
                ApplyEditDots();
            }
            else if (!ObjectGeometry.Contains(shape, ToPointMm(ScreenToMm(screen)), 6 / Scale))
            {
                // A click away from the shape ends editing and does what that click normally does.
                EndPointEdit(commit: true);
                return false;
            }

            e.Handled = true;
            InvalidateVisual();
            return true;
        }

        return false;
    }

    private bool PointToolMouseMove(Point screen)
    {
        if (_drawingShape)
        {
            _hoverDot = DotAt(screen, _newDots.Count > 0 ? _newDots[^1] : null);
            InvalidateVisual();
            return true;
        }

        if (_editShape is not null && _dragDot >= 0)
        {
            _editDots[_dragDot] = DotAt(screen, null);
            ApplyEditDots();
            InvalidateVisual();
            return true;
        }

        if (_editShape is not null)
        {
            Cursor = DotNear(_editDots, screen) >= 0 ? Cursors.SizeAll : null;
        }

        return false;
    }

    private bool PointToolMouseUp()
    {
        if (_editShape is not null && _dragDot >= 0)
        {
            _dragDot = -1;
            ReleaseMouseCapture();
            return true;
        }

        return _drawingShape;
    }

    private bool PointToolKey(KeyEventArgs e)
    {
        if (_drawingShape)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    CancelCustomShape();
                    break;
                case Key.Enter:
                    FinishCustomShape();
                    break;
                case Key.Back or Key.Delete:
                    if (_newDots.Count > 0)
                    {
                        _newDots.RemoveAt(_newDots.Count - 1);
                        PointToolChanged?.Invoke(this, EventArgs.Empty);
                    }

                    break;
                default:
                    return false;
            }

            e.Handled = true;
            InvalidateVisual();
            return true;
        }

        if (_editShape is not null)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    EndPointEdit(commit: false);
                    break;
                case Key.Enter:
                    EndPointEdit(commit: true);
                    break;
                case Key.Back or Key.Delete when _activeDot >= 0 && _editDots.Count > 3:
                    _editDots.RemoveAt(_activeDot);
                    _activeDot = Math.Min(_activeDot, _editDots.Count - 1);
                    ApplyEditDots();
                    break;
                case Key.Back or Key.Delete:
                    // Keep at least three dots; never delete the shape from here.
                    break;
                default:
                    return false;
            }

            e.Handled = true;
            InvalidateVisual();
            return true;
        }

        return false;
    }

    /// <summary>Adds a dot where a line of the shape was double-clicked.</summary>
    private bool InsertDotOnEdge(Point screen)
    {
        int count = _editDots.Count;
        int edges = _editShape!.ClosedPath ? count : count - 1;
        for (int i = 0; i < edges; i++)
        {
            Point a = MmToScreen(_editDots[i].X, _editDots[i].Y);
            Point b = MmToScreen(_editDots[(i + 1) % count].X, _editDots[(i + 1) % count].Y);
            if (DistanceToSegment(screen, a, b) <= DotHitPx)
            {
                Point mm = ScreenToMm(screen);
                _editDots.Insert(i + 1, new PointMm(Math.Round(mm.X, 3), Math.Round(mm.Y, 3)));
                _activeDot = i + 1;
                return true;
            }
        }

        return false;
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        Vector ab = b - a;
        double t = ab.LengthSquared < 1e-9 ? 0 : Math.Clamp(Vector.Multiply(p - a, ab) / ab.LengthSquared, 0, 1);
        return (p - (a + (t * ab))).Length;
    }

    private static PointMm ToPointMm(Point p) => new(p.X, p.Y);

    // ---- drawing ------------------------------------------------------------------------

    /// <summary>The dots and lines of the tool, in screen space on top of everything else.</summary>
    private void DrawPointTool(DrawingContext dc)
    {
        if (_editShape is not null && _session?.ActiveLabel.Objects.Contains(_editShape) != true)
        {
            _editShape = null;
        }

        if (!IsPointToolActive)
        {
            return;
        }

        var accent = new SolidColorBrush(Accent);
        var line = new Pen(accent, 1.5);
        var rubber = new Pen(accent, 1) { DashStyle = MarqueeDash };
        var dotFill = Brushes.White;

        IReadOnlyList<PointMm> dots = _drawingShape ? _newDots : _editDots;
        var screenDots = dots.Select(d => MmToScreen(d.X, d.Y)).ToList();

        if (_drawingShape)
        {
            for (int i = 1; i < screenDots.Count; i++)
            {
                dc.DrawLine(line, screenDots[i - 1], screenDots[i]);
            }

            if (_hoverDot is { } hover && screenDots.Count > 0)
            {
                Point h = MmToScreen(hover.X, hover.Y);
                dc.DrawLine(rubber, screenDots[^1], h);
                if (screenDots.Count >= 2)
                {
                    dc.DrawLine(rubber, h, screenDots[0]);
                }

                DrawBadge(dc, new Point(h.X + 12, h.Y + 12),
                    string.Create(CultureInfo.InvariantCulture, $"{hover.X:0.##}, {hover.Y:0.##} mm"));
            }
        }

        for (int i = 0; i < screenDots.Count; i++)
        {
            // The first dot is the one to click to close the shape, so it stands out.
            bool first = _drawingShape && i == 0 && screenDots.Count >= 3;
            bool active = !_drawingShape && i == _activeDot;
            double r = first ? DotRadiusPx + 2 : DotRadiusPx;
            dc.DrawEllipse(active || first ? accent : dotFill, new Pen(accent, 1.5), screenDots[i], r, r);
        }

        // What to do next, at the top of the canvas.
        var text = new FormattedText(PointToolHint, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 12, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(200, RenderSize.Width - RulerPad - 60),
        };
        var box = new Rect(RulerPad + 12, RulerPad + 10, text.Width + 20, text.Height + 12);
        dc.DrawRoundedRectangle(accent, null, box, 8, 8);
        dc.DrawText(text, new Point(box.X + 10, box.Y + 6));
    }
}
