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
using LabelMaker.Core.Diagnostics;
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

/// <summary>Lining objects up, spacing, sizing, turning, flipping, grouping and stacking them.</summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// Mirrors the selection. An image flips its own pixels (the model carries flip
    /// flags); everything else mirrors its position inside the selection's bounding
    /// box, which is what flipping a group of objects means.
    /// </summary>
    private void Flip(bool horizontal)
    {
        var targets = _session.Selection.Where(o => !o.Locked).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        RectMm union = targets[0].Bounds;
        foreach (LabelObject o in targets.Skip(1))
        {
            union = union.Union(o.Bounds);
        }

        var commands = new List<IUndoableCommand>();
        foreach (LabelObject obj in targets)
        {
            if (obj is ImageObject image)
            {
                ImageAdjustments before = image.Adjustments.Clone();
                ImageAdjustments after = image.Adjustments.Clone();
                if (horizontal)
                {
                    after.FlipHorizontal = !after.FlipHorizontal;
                }
                else
                {
                    after.FlipVertical = !after.FlipVertical;
                }

                commands.Add(new SetPropertyCommand<ImageAdjustments>(
                    "Flip image", () => image.Adjustments, v => image.Adjustments = v, after, (image.Id, "flip")));
                _ = before;
            }

            if (obj is ShapeObject { Shape: ShapeKind.Custom } custom)
            {
                List<PointMm> mirrored = CustomShape.Mirrored(custom.CustomPoints, horizontal);
                commands.Add(new SetPropertyCommand<List<PointMm>>(
                    "Flip shape", () => custom.CustomPoints, v => custom.CustomPoints = v, mirrored, (custom.Id, "flip")));
            }

            if (targets.Count > 1)
            {
                RectMm b = obj.Bounds;
                RectMm mirrored = horizontal
                    ? b with { X = union.X + union.Width - (b.X - union.X) - b.Width }
                    : b with { Y = union.Y + union.Height - (b.Y - union.Y) - b.Height };
                commands.Add(new TransformObjectCommand(obj, mirrored, obj.RotationDegrees));
            }
        }

        if (commands.Count > 0)
        {
            _session.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand("Flip", commands));
            RefreshCanvas();
        }
    }

    private void RotateSelection(double degrees)
    {
        var commands = _session.Selection
            .Where(o => !o.Locked)
            .Select(obj => (IUndoableCommand)new TransformObjectCommand(
                obj, obj.Bounds, ((obj.RotationDegrees + degrees) % 360 + 360) % 360))
            .ToList();

        if (commands.Count > 0)
        {
            _session.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand("Rotate", commands));
            _properties.RefreshAll();
            RefreshCanvas();
        }
    }

    private void GroupSelection()
    {
        string groupId = Guid.NewGuid().ToString("N");
        var commands = _session.Selection
            .Select(o => (IUndoableCommand)new SetPropertyCommand<string?>("Group", () => o.GroupId, v => o.GroupId = v, groupId))
            .ToList();
        _session.Execute(new CompositeCommand("Group", commands));
    }

    private void UngroupSelection()
    {
        var commands = _session.Selection
            .Where(o => o.GroupId is not null)
            .Select(o => (IUndoableCommand)new SetPropertyCommand<string?>("Ungroup", () => o.GroupId, v => o.GroupId = v, null))
            .ToList();
        if (commands.Count > 0)
        {
            _session.Execute(new CompositeCommand("Ungroup", commands));
        }
    }

    private void Reorder(int delta)
    {
        // One command for the whole selection: one command per object, each remembering its
        // index from before the others moved, jumbled the order when several were selected.
        var command = new ArrangeObjectsCommand(_session.ActiveLabel,
            ObjectStacking.Step(_session.ActiveLabel.Objects, _session.Selection.ToList(), delta));
        if (command.ChangesAnything)
        {
            _session.Execute(command);
            RebuildObjectList();
        }
    }

    private enum AlignEdge
    {
        Left,
        CentreX,
        Right,
        Top,
        CentreY,
        Bottom,
    }

    private bool _alignToLabel;

    /// <summary>
    /// When set, alignment uses the label's edges even with several objects selected.
    /// Otherwise one object aligns to the label and several align to each other.
    /// </summary>
    public bool AlignToLabel
    {
        get => _alignToLabel;
        set => SetField(ref _alignToLabel, value);
    }

    /// <summary>
    /// Aligns by what you see: the edges of each object's rotated/skewed outline,
    /// not its un-rotated box, so a tilted barcode lines up with its neighbours.
    /// </summary>
    private void AlignSelection(AlignEdge edge)
    {
        var targets = _session.Selection.Where(o => !o.Locked).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        RectMm reference = targets.Count == 1 || _alignToLabel
            ? _session.ActiveLabel.Bounds
            : _session.Selection.Select(o => o.RotatedBounds).Aggregate((a, b) => a.Union(b));

        var commands = new List<IUndoableCommand>();
        foreach (LabelObject obj in targets)
        {
            RectMm visual = obj.RotatedBounds;
            double dx = edge switch
            {
                AlignEdge.Left => reference.Left - visual.Left,
                AlignEdge.CentreX => reference.Center.X - visual.Center.X,
                AlignEdge.Right => reference.Right - visual.Right,
                _ => 0,
            };
            double dy = edge switch
            {
                AlignEdge.Top => reference.Top - visual.Top,
                AlignEdge.CentreY => reference.Center.Y - visual.Center.Y,
                AlignEdge.Bottom => reference.Bottom - visual.Bottom,
                _ => 0,
            };

            if (Math.Abs(dx) > 1e-6 || Math.Abs(dy) > 1e-6)
            {
                commands.Add(new TransformObjectCommand(obj, obj.Bounds.Offset(dx, dy), obj.RotationDegrees));
            }
        }

        if (commands.Count > 0)
        {
            _session.Execute(new CompositeCommand("Align", commands));
        }
    }

    /// <summary>
    /// Equal gaps between objects: the outermost two stay put and everything in
    /// between is spaced so the space between neighbours is the same.
    /// </summary>
    private void DistributeSelection(bool horizontal)
    {
        var targets = _session.Selection.Where(o => !o.Locked).ToList();
        if (targets.Count < 3)
        {
            return;
        }

        var ordered = targets
            .OrderBy(o => horizontal ? o.RotatedBounds.Center.X : o.RotatedBounds.Center.Y)
            .ToList();

        RectMm first = ordered[0].RotatedBounds;
        RectMm last = ordered[^1].RotatedBounds;
        double span = horizontal ? last.Right - first.Left : last.Bottom - first.Top;
        double sizes = ordered.Sum(o => horizontal ? o.RotatedBounds.Width : o.RotatedBounds.Height);
        double gap = (span - sizes) / (ordered.Count - 1);

        var commands = new List<IUndoableCommand>();
        double cursor = horizontal ? first.Left : first.Top;
        foreach (LabelObject obj in ordered)
        {
            RectMm visual = obj.RotatedBounds;
            double delta = cursor - (horizontal ? visual.Left : visual.Top);
            if (Math.Abs(delta) > 1e-6)
            {
                RectMm moved = horizontal ? obj.Bounds.Offset(delta, 0) : obj.Bounds.Offset(0, delta);
                commands.Add(new TransformObjectCommand(obj, moved, obj.RotationDegrees));
            }

            cursor += (horizontal ? visual.Width : visual.Height) + gap;
        }

        if (commands.Count > 0)
        {
            _session.Execute(new CompositeCommand(horizontal ? "Distribute horizontally" : "Distribute vertically", commands));
        }
    }

    /// <summary>Gives every selected object the width and/or height of the first one selected.</summary>
    private void MatchSize(bool width, bool height)
    {
        if (_session.Selection.Count < 2)
        {
            return;
        }

        RectMm model = _session.Selection[0].Bounds;
        var commands = _session.Selection.Skip(1)
            .Where(o => !o.Locked)
            .Select(o =>
            {
                RectMm b = o.Bounds;
                var size = new SizeMm(width ? model.Width : b.Width, height ? model.Height : b.Height);
                return (IUndoableCommand)new TransformObjectCommand(o, RectMm.FromCenter(b.Center, size), o.RotationDegrees);
            })
            .ToList();

        if (commands.Count > 0)
        {
            _session.Execute(new CompositeCommand("Match size", commands));
        }
    }
}
