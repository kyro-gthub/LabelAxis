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

/// <summary>Mouse and keyboard: selecting, dragging, resizing, rotating, skewing, guides and nudging.</summary>
public sealed partial class DesignerCanvas
{
    // ---- interaction ---------------------------------------------------

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        // Shift scrolls sideways, Ctrl scrolls up and down; the plain wheel zooms to the pointer.
        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            ScrollTo(horizontal: true, HorizontalScroll.Value - e.Delta);
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            ScrollTo(horizontal: false, VerticalScroll.Value - e.Delta);
        }
        else
        {
            ZoomAt(e.Delta > 0 ? 1.15 : 1 / 1.15, e.GetPosition(this));
        }

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

        // The custom shape tool takes clicks while it is on.
        if (IsPointToolActive && PointToolMouseDown(e, screen))
        {
            return;
        }

        // Ruler drag -> new guide (only the ruler strips themselves; never competes with objects)
        if (RulerPad > 0 && !(_session.ActiveGuides.Locked))
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

        if (hit is null && Settings?.ShowGuides != false && !_session.ActiveGuides.Locked &&
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

        if (e.ClickCount == 2 && hit is ShapeObject { Shape: ShapeKind.Custom } custom && !custom.Locked)
        {
            BeginPointEdit(custom);
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

        if (IsPointToolActive && PointToolMouseMove(screen))
        {
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
            RectMm to = ResizeRect(groupStart, _activeHandle, dx, dy, proportional: shift, fromCentre: ctrl);
            var groupLines = new List<SnapLine>(2);
            if (!shift && !ctrl)
            {
                to = SnapResizedRect(to, groupLines);
            }

            ResizeGroup(groupStart, to);
            KeepTextInsideBoxes();
            FitBarcodesLive();
            _activeSnapLines = groupLines;
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

        if (_resizing)
        {
            KeepTextInsideBoxes();
            FitBarcodesLive();
        }

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

        if (!_panning && IsPointToolActive && PointToolMouseUp())
        {
            e.Handled = true;
            return;
        }

        if (_guideDrag is { } g && _session is not null)
        {
            var guides = _session.ActiveGuides;
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
                if (obj is BarcodeObject barcode && _dragStartSpecs.TryGetValue(obj.Id, out LabelMaker.Core.Barcodes.BarcodeSpec? startSpec))
                {
                    barcode.Spec = startSpec; // the edit rescales it again, as one undo step
                }

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
        _dragStartSpecs.Clear();
        _dragStartRotation.Clear();
        _dragStartSkew.Clear();
        _dragStartTextRoom.Clear();
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

        if (IsPointToolActive && PointToolKey(e))
        {
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

}
