using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Units;

namespace LabelMaker.Core.Editing;

/// <summary>
/// Where pasted objects land. The copied objects move and scale as one block, so their
/// layout survives the trip to another page or project: pasting into a smaller label used
/// to push each object against the edges separately, piling them on top of each other.
/// </summary>
public static class PastePlacement
{
    /// <summary>Step used when a paste would land exactly on top of the objects it was copied from.</summary>
    public const double CascadeMm = 3.0;

    /// <summary>
    /// Fresh copies of <paramref name="items"/> (new ids, groups kept but renamed) placed on
    /// <paramref name="target"/>: at <paramref name="atMm"/> when given, otherwise where they
    /// were, stepped down and right while that spot is already taken by identical objects.
    /// A block larger than the label is scaled down to fit, text sizes included.
    /// </summary>
    public static IReadOnlyList<LabelObject> Place(IReadOnlyList<LabelObject> items, LabelDocument target, PointMm? atMm = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(target);
        if (items.Count == 0)
        {
            return Array.Empty<LabelObject>();
        }

        var groups = new Dictionary<string, string>();
        List<LabelObject> copies = items.Select(item =>
        {
            LabelObject copy = item.Clone();
            if (item.GroupId is { } id)
            {
                copy.GroupId = groups.TryGetValue(id, out string? renamed) ? renamed : groups[id] = Guid.NewGuid().ToString("N");
            }

            return copy;
        }).ToList();

        RectMm block = BlockBounds(copies);
        SizeMm label = target.SizeMm;

        // Too big for this label: shrink the whole block about its top-left corner.
        double scale = Math.Min(1.0, Math.Min(
            block.Width > 0.01 ? label.Width / block.Width : 1.0,
            block.Height > 0.01 ? label.Height / block.Height : 1.0));
        if (scale < 0.999)
        {
            foreach (LabelObject copy in copies)
            {
                ScaleAbout(copy, block.X, block.Y, scale);
            }

            block = BlockBounds(copies);
        }

        double dx = 0, dy = 0;
        if (atMm is { } at)
        {
            dx = at.X - block.X;
            dy = at.Y - block.Y;
        }
        else
        {
            // Same spot as objects already on this label (pasting back onto the page it was
            // copied from, or pasting twice): step down and right until the spot is free.
            for (int step = 0; step < 50 && copies.Any(c => Occupied(target, c, dx, dy)); step++)
            {
                dx += CascadeMm;
                dy += CascadeMm;
            }
        }

        // Keep the block on the label, moving it as a whole.
        dx += Shift(block.X + dx, block.Width, label.Width);
        dy += Shift(block.Y + dy, block.Height, label.Height);

        foreach (LabelObject copy in copies)
        {
            copy.Bounds = copy.Bounds.Offset(dx, dy);
        }

        return copies;
    }

    private static bool Occupied(LabelDocument target, LabelObject copy, double dx, double dy)
    {
        RectMm moved = copy.Bounds.Offset(dx, dy);
        return target.Objects.Any(o => o.Kind == copy.Kind &&
                                       Math.Abs(o.Bounds.X - moved.X) < 0.01 && Math.Abs(o.Bounds.Y - moved.Y) < 0.01 &&
                                       Math.Abs(o.Bounds.Width - moved.Width) < 0.01 && Math.Abs(o.Bounds.Height - moved.Height) < 0.01);
    }

    /// <summary>How far to move a span starting at <paramref name="start"/> so it lies within [0, extent].</summary>
    private static double Shift(double start, double length, double extent)
    {
        if (start + length > extent)
        {
            return Math.Max(-start, extent - (start + length));
        }

        return start < 0 ? -start : 0;
    }

    private static RectMm BlockBounds(IReadOnlyList<LabelObject> objects)
    {
        RectMm box = ObjectGeometry.Bounds(objects[0]);
        foreach (LabelObject obj in objects.Skip(1))
        {
            box = box.Union(ObjectGeometry.Bounds(obj));
        }

        return box;
    }

    private static void ScaleAbout(LabelObject obj, double originX, double originY, double scale)
    {
        RectMm b = obj.Bounds;
        obj.Bounds = new RectMm(
            originX + ((b.X - originX) * scale),
            originY + ((b.Y - originY) * scale),
            b.Width * scale,
            b.Height * scale);

        switch (obj)
        {
            case TextObject text:
                text.FontSizePt *= scale;
                text.MinAutoFitPt = Math.Min(text.MinAutoFitPt, text.FontSizePt);
                foreach (Model.Text.TextRun run in text.Content.Paragraphs.SelectMany(p => p.Runs))
                {
                    if (run.FontSizePt is { } size)
                    {
                        run.FontSizePt = size * scale;
                    }
                }

                break;
            case TableObject table:
                table.FontSizePt *= scale;
                break;
            case ShapeObject shape:
                shape.StrokeWidthMm *= scale;
                break;
        }
    }
}
