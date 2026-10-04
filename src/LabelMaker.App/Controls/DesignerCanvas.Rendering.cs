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

/// <summary>Drawing the label, grid, selection handles, groups, guides, snap lines and rulers.</summary>
public sealed partial class DesignerCanvas
{
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

        // While a custom shape's dots are edited, its resize handles would sit on top of the dots.
        if (_editShape is null)
        {
            DrawSelection(dc);
        }

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

        DrawPointTool(dc);

        // Every change of zoom or position ends up here, so the scroll bars follow from one place.
        Dispatcher.BeginInvoke(RaiseScrollChanged, System.Windows.Threading.DispatcherPriority.Render);
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

        foreach (double xMm in _session!.ActiveGuides.VerticalMm)
        {
            double x = MmToScreen(xMm, 0).X;
            if (x >= left)
            {
                dc.DrawLine(pen, new Point(x, top), new Point(x, ActualHeight));
            }
        }

        foreach (double yMm in _session.ActiveGuides.HorizontalMm)
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

}
