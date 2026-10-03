using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Printing;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;

namespace LabelMaker.Core.Editing;

/// <summary>What of an object's placement is copied to the matching objects on other pages.</summary>
[Flags]
public enum LayoutParts
{
    None = 0,

    /// <summary>Where the object's top-left corner is.</summary>
    Position = 1,

    /// <summary>Width and height.</summary>
    Size = 2,

    /// <summary>Rotation and slant.</summary>
    Rotation = 4,
}

/// <summary>How an object on this page finds "the same" object on another page.</summary>
public enum LayoutMatch
{
    /// <summary>The object of the same kind with the same name (pages duplicated from another keep their objects' names).</summary>
    SameName = 0,

    /// <summary>The same kind in the same place in the stacking order: first text box to first text box, second barcode to second barcode.</summary>
    SameOrder = 1,
}

/// <summary>
/// Puts objects on other pages where they are on this page: the text, barcodes and images of
/// a set of similar pages line up without moving each one by hand.
/// </summary>
public static class LayoutCopy
{
    /// <summary>
    /// Each source object paired with the objects on the given pages (Pages panel positions,
    /// 0 = the master label) that would be moved to match it. Locked objects, the source page
    /// itself and kinds not in <paramref name="kinds"/> are left out.
    /// </summary>
    public static IReadOnlyList<(LabelObject Source, LabelObject Target)> Pairs(
        ProjectModel project, LabelDocument sourcePage, IEnumerable<LabelObject> sources, IEnumerable<int> pagePositions,
        IReadOnlySet<ObjectKind> kinds, LayoutMatch match)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(sourcePage);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(pagePositions);
        ArgumentNullException.ThrowIfNull(kinds);

        List<LabelObject> from = sources.Where(o => kinds.Contains(o.Kind)).Distinct().ToList();
        var wanted = new HashSet<int>(pagePositions);
        var pairs = new List<(LabelObject, LabelObject)>();

        foreach (LabelDocument page in PrintablePages.All(project).Where((_, i) => wanted.Contains(i)))
        {
            if (ReferenceEquals(page, sourcePage))
            {
                continue;
            }

            foreach (LabelObject source in from)
            {
                if (Match(sourcePage, source, page, match) is { Locked: false } target)
                {
                    pairs.Add((source, target));
                }
            }
        }

        return pairs;
    }

    /// <summary>
    /// One undo step that moves every target to its source's place, or null when nothing would
    /// change. Grow-to-fit text boxes given a new width are made tall enough for their text when
    /// <paramref name="measurer"/> is given.
    /// </summary>
    public static IUndoableCommand? Build(IEnumerable<(LabelObject Source, LabelObject Target)> pairs, LayoutParts parts, ITextMeasurer? measurer)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        var commands = new List<IUndoableCommand>();
        foreach ((LabelObject source, LabelObject target) in pairs)
        {
            RectMm b = target.Bounds;
            if (parts.HasFlag(LayoutParts.Size))
            {
                b = b with { Width = source.Bounds.Width, Height = source.Bounds.Height };
            }

            if (parts.HasFlag(LayoutParts.Position))
            {
                b = b with { X = source.Bounds.X, Y = source.Bounds.Y };
            }

            if (measurer is not null && target is TextObject { AutoFit: TextAutoFit.ResizeBox } text)
            {
                // Measure at the new width without touching the box yet.
                RectMm old = text.Bounds;
                text.Bounds = b;
                double needed = Math.Round(TextBoxFit.RequiredHeightMm(text, measurer), 2);
                text.Bounds = old;
                if (needed > b.Height + 0.01)
                {
                    b = b with { Height = needed };
                }
            }

            bool rotate = parts.HasFlag(LayoutParts.Rotation);
            double rotation = rotate ? source.RotationDegrees : target.RotationDegrees;
            double skewX = rotate ? source.SkewXDegrees : target.SkewXDegrees;
            double skewY = rotate ? source.SkewYDegrees : target.SkewYDegrees;

            if (b != target.Bounds || rotation != target.RotationDegrees || skewX != target.SkewXDegrees || skewY != target.SkewYDegrees)
            {
                commands.Add(new TransformObjectCommand(target, b, rotation, skewX, skewY));
            }
        }

        return commands.Count == 0 ? null
            : new CompositeCommand(commands.Count == 1 ? "Match position on 1 object" : $"Match positions on {commands.Count} objects", commands);
    }

    private static LabelObject? Match(LabelDocument sourcePage, LabelObject source, LabelDocument page, LayoutMatch match)
    {
        if (match == LayoutMatch.SameName)
        {
            return string.IsNullOrWhiteSpace(source.Name)
                ? null
                : page.Objects.FirstOrDefault(o => o.Kind == source.Kind &&
                                                   string.Equals(o.Name.Trim(), source.Name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        int index = sourcePage.Objects.Where(o => o.Kind == source.Kind).ToList().IndexOf(source);
        return index < 0 ? null : page.Objects.Where(o => o.Kind == source.Kind).ElementAtOrDefault(index);
    }
}
