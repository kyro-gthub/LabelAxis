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

/// <summary>Snapping and hit testing.</summary>
public sealed partial class DesignerCanvas
{
    // ---- snapping ----------------------------------------------------

    /// <summary>Snap tolerance in document mm, with a small floor so it stays usable at very high zoom.</summary>
    private double SnapToleranceMm() => Math.Max((Settings?.SnapPixels ?? 10.0) / Scale, 0.12);

    private bool SnapsToGuides => Settings is null || (Settings.SnapToGuides && Settings.ShowGuides);

    private double GridStepMm => Settings is { SnapToGrid: true, GridSizeMm: > 0 } s ? s.GridSizeMm : 0;

    private (IReadOnlyList<double> V, IReadOnlyList<double> H) SnapTargets()
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

        // Hidden guides do not pull objects around.
        if (SnapsToGuides)
        {
            v.AddRange(_session.ActiveGuides.VerticalMm);
            h.AddRange(_session.ActiveGuides.HorizontalMm);
        }

        if (Settings?.SnapToObjects != false)
        {
            foreach (LabelObject o in label.Objects)
            {
                if (_session.Selection.Contains(o) || !o.Visible)
                {
                    continue;
                }

                RectMm b = ObjectGeometry.Bounds(o);
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

    /// <summary>
    /// Moves the selection onto guides, label edges and other objects; an axis that finds
    /// nothing nearby snaps to the grid instead. Each axis is decided on its own, so a
    /// box that lines up with a guide horizontally still steps along the grid vertically,
    /// and several objects or a group snap together as one block.
    /// </summary>
    private IReadOnlyList<SnapLine> ApplyMoveSnap()
    {
        List<LabelObject> moving = _session!.Selection.Where(o => !o.Locked).ToList();
        if (moving.Count == 0)
        {
            return Array.Empty<SnapLine>();
        }

        RectMm block = ObjectGeometry.Bounds(moving[0]);
        foreach (LabelObject o in moving.Skip(1))
        {
            block = block.Union(ObjectGeometry.Bounds(o));
        }

        (IReadOnlyList<double> v, IReadOnlyList<double> hh) = SnapTargets();
        SnapResult r = SnapCalculator.SnapMove(block, v, hh, SnapToleranceMm());
        double dx = r.Dx, dy = r.Dy;

        double grid = GridStepMm;
        if (grid > 0)
        {
            if (!r.Lines.Any(l => l.Vertical))
            {
                dx = (Math.Round(block.X / grid) * grid) - block.X;
            }

            if (!r.Lines.Any(l => !l.Vertical))
            {
                dy = (Math.Round(block.Y / grid) * grid) - block.Y;
            }
        }

        if (Math.Abs(dx) > 1e-9 || Math.Abs(dy) > 1e-9)
        {
            foreach (LabelObject o in moving)
            {
                o.Bounds = o.Bounds.Offset(dx, dy);
            }
        }

        return r.Lines;
    }

    /// <summary>Snaps the edges a resize handle moves: to guides and objects first, otherwise to the grid.</summary>
    private RectMm SnapResizedRect(RectMm b, List<SnapLine> lines)
    {
        (IReadOnlyList<double> v, IReadOnlyList<double> hh) = SnapTargets();
        double tolMm = SnapToleranceMm();
        double grid = GridStepMm;

        bool left = _activeHandle is Handle.Left or Handle.TopLeft or Handle.BottomLeft;
        bool right = _activeHandle is Handle.Right or Handle.TopRight or Handle.BottomRight;
        bool top = _activeHandle is Handle.Top or Handle.TopLeft or Handle.TopRight;
        bool bottom = _activeHandle is Handle.Bottom or Handle.BottomLeft or Handle.BottomRight;

        double Edge(double value, bool vertical, IReadOnlyList<double> targets)
        {
            double snapped = SnapCalculator.SnapValue(value, targets, tolMm, out bool hit);
            if (hit)
            {
                lines.Add(new SnapLine(vertical, snapped));
                return snapped;
            }

            return grid > 0 ? Math.Round(value / grid) * grid : value;
        }

        double l = left ? Edge(b.Left, true, v) : b.Left;
        double rr = right ? Edge(b.Right, true, v) : b.Right;
        double t = top ? Edge(b.Top, false, hh) : b.Top;
        double bb = bottom ? Edge(b.Bottom, false, hh) : b.Bottom;

        return rr - l >= 0.5 && bb - t >= 0.5 ? RectMm.FromLtrb(l, t, rr, bb) : b;
    }

    private static readonly WpfTextMeasurer FitMeasurer = new();

    /// <summary>
    /// Keeps grow-to-fit text boxes fitted to their text while a handle is dragged.
    /// A left or right handle changes the width, so the text re-wraps and the height follows
    /// it both ways: taller when the box gets narrower, shorter again when it gets wider. Any
    /// extra room the box had below its text when the drag began is kept. Every other handle
    /// sets the height itself, so there the box only stops short of cutting the text off (the
    /// top edge stops when a top handle is dragged).
    /// </summary>
    private void KeepTextInsideBoxes()
    {
        bool fromTop = _activeHandle is Handle.TopLeft or Handle.Top or Handle.TopRight;
        bool sideways = _activeHandle is Handle.Left or Handle.Right;
        foreach (TextObject text in _session!.Selection.OfType<TextObject>())
        {
            if (text.Locked || text.AutoFit != LabelMaker.Core.Model.Text.TextAutoFit.ResizeBox)
            {
                continue;
            }

            double needed = TextBoxFit.RequiredHeightMm(text, FitMeasurer);
            RectMm b = text.Bounds;
            if (sideways)
            {
                double target = needed + _dragStartTextRoom.GetValueOrDefault(text.Id);
                if (Math.Abs(target - b.Height) > 0.005)
                {
                    text.Bounds = WithHeightKeepingTop(text, b, target);
                }
            }
            else if (b.Height < needed)
            {
                text.Bounds = fromTop ? b with { Y = b.Bottom - needed, Height = needed } : b with { Height = needed };
            }
        }
    }

    /// <summary>
    /// <paramref name="b"/> with a new height and its top edge left where it is on screen,
    /// measured along the object's own axes so a rotated box does not slide sideways.
    /// </summary>
    private static RectMm WithHeightKeepingTop(LabelObject obj, RectMm b, double height)
    {
        if (ObjectGeometry.IsAxisAligned(obj))
        {
            return b with { Height = height };
        }

        PointMm shift = ObjectGeometry.Linear(obj).ApplyVector(0, (height - b.Height) / 2.0);
        return RectMm.FromCenter(new PointMm(b.Center.X + shift.X, b.Center.Y + shift.Y), new SizeMm(b.Width, height));
    }

    private IReadOnlyList<SnapLine> ApplyResizeSnap()
    {
        if (_session!.Selection.Count != 1 || _session.Selection[0].Locked)
        {
            return Array.Empty<SnapLine>();
        }

        LabelObject obj = _session.Selection[0];
        var lines = new List<SnapLine>(2);
        obj.Bounds = SnapResizedRect(obj.Bounds, lines);
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

        var guides = _session.ActiveGuides;
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

        if (Settings?.ShowGuides != false && !_session.ActiveGuides.Locked && TryHitGuide(screen, out GuideDrag g))
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
        _dragStartSpecs = _session.Selection.OfType<BarcodeObject>().Where(b => b.AutoSize).ToDictionary(b => b.Id, b => b.Spec);
        _dragStartTextRoom = _session.Selection.OfType<TextObject>()
            .Where(t => t.AutoFit == LabelMaker.Core.Model.Text.TextAutoFit.ResizeBox)
            .ToDictionary(t => t.Id, t => Math.Max(0, t.Bounds.Height - TextBoxFit.RequiredHeightMm(t, FitMeasurer)));
    }

    /// <summary>
    /// While a box is being resized, a natural-size barcode is rescaled to fill it, so the bars
    /// and the box change together under the mouse (the edit itself is made on release).
    /// </summary>
    private void FitBarcodesLive()
    {
        if (_session is null || _dragStartSpecs.Count == 0)
        {
            return;
        }

        var resolver = new LabelMaker.Core.Variables.VariableResolver(_session.CurrentResolveContext());
        foreach (BarcodeObject barcode in _session.Selection.OfType<BarcodeObject>())
        {
            if (barcode.Locked || !_dragStartSpecs.TryGetValue(barcode.Id, out LabelMaker.Core.Barcodes.BarcodeSpec? start) ||
                BarcodeFit.ToBox(start, barcode.Bounds, resolver.Resolve(start.Data)) is not { } fitted)
            {
                continue;
            }

            barcode.Spec = fitted.Spec;
            barcode.Bounds = fitted.Bounds;
        }
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
