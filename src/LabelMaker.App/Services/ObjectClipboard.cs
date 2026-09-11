using LabelMaker.Core.Model;

namespace LabelMaker.App.Services;

/// <summary>
/// In-app clipboard for label objects (Copy / Cut / Paste from the canvas
/// context menu and Ctrl+C/X/V). Stores deep clones so later edits to the
/// originals don't affect what gets pasted.
/// </summary>
public static class ObjectClipboard
{
    private static readonly List<LabelObject> Items = new();

    public static bool HasContent => Items.Count > 0;

    public static int Count => Items.Count;

    public static void Set(IEnumerable<LabelObject> objects)
    {
        Items.Clear();
        Items.AddRange(objects.Select(o => o.Clone()));
    }

    /// <summary>Fresh clones of the clipboard contents, offset by <paramref name="offsetMm"/>.</summary>
    public static IReadOnlyList<LabelObject> Paste(double offsetMm = 3.0)
    {
        var result = new List<LabelObject>(Items.Count);
        foreach (LabelObject item in Items)
        {
            LabelObject copy = item.Clone();
            copy.Bounds = copy.Bounds.Offset(offsetMm, offsetMm);
            copy.GroupId = null;
            result.Add(copy);
        }

        return result;
    }

    /// <summary>Clones placed so the group's top-left sits at <paramref name="atMm"/>.</summary>
    public static IReadOnlyList<LabelObject> PasteAt(Core.Units.PointMm atMm)
    {
        if (Items.Count == 0)
        {
            return Array.Empty<LabelObject>();
        }

        double minX = Items.Min(o => o.Bounds.X);
        double minY = Items.Min(o => o.Bounds.Y);
        var result = new List<LabelObject>(Items.Count);
        foreach (LabelObject item in Items)
        {
            LabelObject copy = item.Clone();
            copy.Bounds = copy.Bounds.Offset(atMm.X - minX, atMm.Y - minY);
            copy.GroupId = null;
            result.Add(copy);
        }

        return result;
    }
}
