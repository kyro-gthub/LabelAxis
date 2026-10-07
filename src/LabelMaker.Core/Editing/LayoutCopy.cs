using LabelMaker.Core.Barcodes;
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

    /// <summary>By name where a page has an object of that name, and by order for the rest.</summary>
    NameThenOrder = 2,
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

        var pairs = new List<(LabelObject, LabelObject)>();
        foreach ((LabelDocument page, List<(LabelObject Source, LabelObject? Target)> matches) in MatchPages(project, sourcePage, sources, pagePositions, kinds, match))
        {
            foreach ((LabelObject source, LabelObject? target) in matches)
            {
                if (target is { Locked: false })
                {
                    pairs.Add((source, target));
                }
            }
        }

        return pairs;
    }

    /// <summary>
    /// The source objects that have nothing to match on each of the given pages — what "add
    /// what a page is missing" would copy there.
    /// </summary>
    public static IReadOnlyList<(LabelObject Source, LabelDocument Page)> Unmatched(
        ProjectModel project, LabelDocument sourcePage, IEnumerable<LabelObject> sources, IEnumerable<int> pagePositions,
        IReadOnlySet<ObjectKind> kinds, LayoutMatch match)
    {
        var missing = new List<(LabelObject, LabelDocument)>();
        foreach ((LabelDocument page, List<(LabelObject Source, LabelObject? Target)> matches) in MatchPages(project, sourcePage, sources, pagePositions, kinds, match))
        {
            missing.AddRange(matches.Where(m => m.Target is null).Select(m => (m.Source, page)));
        }

        return missing;
    }

    private static IEnumerable<(LabelDocument Page, List<(LabelObject Source, LabelObject? Target)> Matches)> MatchPages(
        ProjectModel project, LabelDocument sourcePage, IEnumerable<LabelObject> sources, IEnumerable<int> pagePositions,
        IReadOnlySet<ObjectKind> kinds, LayoutMatch match)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(sourcePage);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(pagePositions);
        ArgumentNullException.ThrowIfNull(kinds);

        var picked = new HashSet<LabelObject>(sources.Where(o => kinds.Contains(o.Kind)));
        // In stacking order, so "the 2nd barcode" means the same on every page.
        List<LabelObject> from = sourcePage.Objects.Where(picked.Contains).Concat(picked.Where(o => !sourcePage.Objects.Contains(o))).ToList();
        var wanted = new HashSet<int>(pagePositions);

        foreach (LabelDocument page in PrintablePages.All(project).Where((_, i) => wanted.Contains(i)))
        {
            if (!ReferenceEquals(page, sourcePage))
            {
                yield return (page, MatchPage(sourcePage, from, page, match));
            }
        }
    }

    // Each source gets at most one object of its own kind on the page, and no object is taken twice.
    private static List<(LabelObject Source, LabelObject? Target)> MatchPage(LabelDocument sourcePage, List<LabelObject> sources, LabelDocument page, LayoutMatch match)
    {
        var result = sources.Select(s => (Source: s, Target: (LabelObject?)null)).ToList();
        var taken = new HashSet<LabelObject>();

        if (match != LayoutMatch.SameOrder)
        {
            for (int i = 0; i < result.Count; i++)
            {
                LabelObject source = result[i].Source;
                if (!string.IsNullOrWhiteSpace(source.Name) &&
                    page.Objects.FirstOrDefault(o => !taken.Contains(o) && Family(o) == Family(source) &&
                                                     string.Equals(o.Name.Trim(), source.Name.Trim(), StringComparison.OrdinalIgnoreCase)) is { } named)
                {
                    result[i] = (source, named);
                    taken.Add(named);
                }
            }
        }

        if (match == LayoutMatch.SameOrder)
        {
            for (int i = 0; i < result.Count; i++)
            {
                LabelObject source = result[i].Source;
                int index = sourcePage.Objects.Where(o => Family(o) == Family(source)).ToList().IndexOf(source);
                if (index >= 0 && page.Objects.Where(o => Family(o) == Family(source)).ElementAtOrDefault(index) is { } same)
                {
                    result[i] = (source, same);
                }
            }
        }
        else if (match == LayoutMatch.NameThenOrder)
        {
            // What found no namesake pairs up, in order, with what the page has left of the same kind.
            var unmatched = result.Select((r, i) => (r.Source, Index: i)).Where(r => result[r.Index].Target is null).ToList();
            foreach (var family in unmatched.GroupBy(r => Family(r.Source)))
            {
                List<LabelObject> left = page.Objects.Where(o => !taken.Contains(o) && Family(o) == family.Key).ToList();
                foreach (((LabelObject source, int index), LabelObject target) in family.Zip(left))
                {
                    result[index] = (source, target);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// One undo step that takes the given objects off whichever pages of the project hold them
    /// (the master label too), or null when none of them is on a page. Locked objects stay.
    /// </summary>
    public static IUndoableCommand? Remove(ProjectModel project, IEnumerable<LabelObject> objects)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(objects);

        var gone = new HashSet<LabelObject>(objects.Where(o => !o.Locked));
        var commands = new List<IUndoableCommand>();
        foreach (LabelDocument page in PrintablePages.All(project))
        {
            List<LabelObject> here = page.Objects.Where(gone.Contains).ToList();
            if (here.Count > 0)
            {
                commands.Add(new RemoveObjectsCommand(page, here));
            }
        }

        return commands.Count == 0 ? null : new CompositeCommand("Remove from all pages", commands);
    }

    // A QR code and a line barcode are both barcodes, but never "the same" object.
    private static (ObjectKind Kind, bool TwoD) Family(LabelObject o) =>
        (o.Kind, o is BarcodeObject { Spec.Symbology: var s } && s.Is2D());

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
}
