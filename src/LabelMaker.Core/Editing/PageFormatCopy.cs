using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;

namespace LabelMaker.Core.Editing;

/// <summary>What of this page's objects is copied to the matching objects on other pages.</summary>
[Flags]
public enum PageFormatParts
{
    None = 0,

    /// <summary>Where the object's top-left corner is.</summary>
    Position = 1,

    /// <summary>Width and height.</summary>
    Size = 2,

    /// <summary>Rotation and slant.</summary>
    Rotation = 4,

    /// <summary>
    /// How it looks and its settings: font and colours, barcode type, bar height and caption,
    /// image fit and border, shape outline and fill, table lines. What each page says — its
    /// words, barcode value, picture and table cells — stays its own.
    /// </summary>
    Look = 8,

    /// <summary>Objects a page has nothing to match with are copied onto it, words and all.</summary>
    AddMissing = 16,
}

/// <summary>
/// Makes other pages look like this one: every text box, barcode, image, shape and table on
/// the ticked pages takes the place, size and look of its match on this page, in one undo step,
/// while keeping its own text, barcode value and picture.
/// </summary>
public static class PageFormatCopy
{
    /// <summary>
    /// One undo step for the matched <paramref name="pairs"/> and (with
    /// <see cref="PageFormatParts.AddMissing"/>) the <paramref name="missing"/> objects, or null
    /// when nothing would change. <paramref name="textParts"/> picks which parts of a text box's
    /// look are copied. Grow-to-fit text boxes are made tall enough for their text when
    /// <paramref name="measurer"/> is given.
    /// </summary>
    public static IUndoableCommand? Build(
        IEnumerable<(LabelObject Source, LabelObject Target)> pairs,
        IEnumerable<(LabelObject Source, LabelDocument Page)> missing,
        IReadOnlyList<LabelDocument> pages,
        PageFormatParts parts,
        TextStyleParts textParts,
        ITextMeasurer? measurer)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentNullException.ThrowIfNull(missing);
        ArgumentNullException.ThrowIfNull(pages);

        var commands = new List<IUndoableCommand>();
        int changed = 0;
        foreach ((LabelObject source, LabelObject target) in pairs)
        {
            if (pages.FirstOrDefault(p => p.Objects.Contains(target)) is not { } page)
            {
                continue;
            }

            LabelObject styled = Styled(source, target, parts, textParts, measurer);
            if (!Same(styled, target))
            {
                commands.Add(new ReplaceObjectCommand(page, target, styled));
                changed++;
            }
        }

        int added = 0;
        if (parts.HasFlag(PageFormatParts.AddMissing))
        {
            // On top of what each page has, in this page's stacking order.
            var next = new Dictionary<LabelDocument, int>();
            foreach ((LabelObject source, LabelDocument page) in missing)
            {
                LabelObject copy = source.Clone();
                copy.Locked = false;
                int index = next.TryGetValue(page, out int i) ? i : page.Objects.Count;
                commands.Add(new AddObjectCommand(page, copy, index));
                next[page] = index + 1;
                added++;
            }
        }

        if (commands.Count == 0)
        {
            return null;
        }

        string label = (changed, added) switch
        {
            (_, 0) => changed == 1 ? "Apply page format to 1 object" : $"Apply page format to {changed} objects",
            (0, _) => added == 1 ? "Add 1 object to other pages" : $"Add {added} objects to other pages",
            _ => $"Apply page format to {changed + added} objects",
        };
        return new CompositeCommand(label, commands);
    }

    /// <summary>The target as it will be: its own content, with the chosen parts taken from the source.</summary>
    public static LabelObject Styled(LabelObject source, LabelObject target, PageFormatParts parts, TextStyleParts textParts, ITextMeasurer? measurer)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        LabelObject styled = parts.HasFlag(PageFormatParts.Look) && source.GetType() == target.GetType()
            ? WithLook(source, target, textParts)
            : target.Clone();

        // Still the same object: its id (record overrides and groups refer to it), name and lock.
        styled.Id = target.Id;
        styled.Name = target.Name;
        styled.GroupId = target.GroupId;
        styled.Locked = target.Locked;
        styled.Visible = target.Visible;

        RectMm b = target.Bounds;
        if (parts.HasFlag(PageFormatParts.Size))
        {
            b = b with { Width = source.Bounds.Width, Height = source.Bounds.Height };
        }

        if (parts.HasFlag(PageFormatParts.Position))
        {
            b = b with { X = source.Bounds.X, Y = source.Bounds.Y };
        }

        styled.Bounds = b;
        bool rotate = parts.HasFlag(PageFormatParts.Rotation);
        styled.RotationDegrees = rotate ? source.RotationDegrees : target.RotationDegrees;
        styled.SkewXDegrees = rotate ? source.SkewXDegrees : target.SkewXDegrees;
        styled.SkewYDegrees = rotate ? source.SkewYDegrees : target.SkewYDegrees;

        if (measurer is not null && styled is TextObject { AutoFit: TextAutoFit.ResizeBox } text)
        {
            double needed = Math.Round(TextBoxFit.RequiredHeightMm(text, measurer), 2);
            if (needed > text.Bounds.Height + 0.01)
            {
                text.Bounds = text.Bounds with { Height = needed };
            }
        }

        return styled;
    }

    private static LabelObject WithLook(LabelObject source, LabelObject target, TextStyleParts textParts)
    {
        switch ((source, target))
        {
            case (TextObject from, TextObject to):
            {
                var text = (TextObject)to.Clone();
                if (textParts == TextStyleParts.All && from.Content.PlainText == to.Content.PlainText)
                {
                    // The same words: the per-word formatting comes too.
                    text.Content = from.Content.Clone();
                }

                TextStyleCopy.Apply(from, text, textParts);
                text.Direction = from.Direction;
                text.WordWrap = from.WordWrap;
                text.AutoFit = from.AutoFit;
                text.PaddingMm = from.PaddingMm;
                text.Opacity = from.Opacity;
                return text;
            }

            case (BarcodeObject from, BarcodeObject to):
            {
                var barcode = (BarcodeObject)from.Clone();
                barcode.Spec.Data = to.Spec.Data;
                // A barcode type that cannot hold this page's value (EAN-13 for "ABC") is not forced on it.
                if (!to.Spec.Data.Contains('{') &&
                    !BarcodeValidator.Validate(barcode.Spec, to.Spec.Data).IsValid &&
                    BarcodeValidator.Validate(to.Spec, to.Spec.Data).IsValid)
                {
                    barcode.Spec.Symbology = to.Spec.Symbology;
                    barcode.Spec.AddCheckDigit = to.Spec.AddCheckDigit;
                    barcode.Spec.Gs1Mode = to.Spec.Gs1Mode;
                }

                return barcode;
            }

            case (ImageObject from, ImageObject to):
            {
                var image = (ImageObject)from.Clone();
                image.AssetId = to.AssetId;
                image.SourceFileName = to.SourceFileName;
                image.SourcePixelWidth = to.SourcePixelWidth;
                image.SourcePixelHeight = to.SourcePixelHeight;
                // The crop and flips belong to the picture; brightness, contrast and grey are the look.
                ImageAdjustments own = to.Adjustments;
                image.Adjustments.CropX = own.CropX;
                image.Adjustments.CropY = own.CropY;
                image.Adjustments.CropWidth = own.CropWidth;
                image.Adjustments.CropHeight = own.CropHeight;
                image.Adjustments.FlipHorizontal = own.FlipHorizontal;
                image.Adjustments.FlipVertical = own.FlipVertical;
                return image;
            }

            case (TableObject from, TableObject to):
            {
                var table = (TableObject)from.Clone();
                table.Rows = to.Rows;
                table.Columns = to.Columns;
                table.Cells = new List<string>(to.Cells);
                if (from.Rows != to.Rows || from.Columns != to.Columns)
                {
                    table.ColumnWeights = new List<double>(to.ColumnWeights);
                    table.RowWeights = new List<double>(to.RowWeights);
                }

                return table;
            }

            default:
                // A shape is all look.
                return source.Clone();
        }
    }

    // Compared through the saved form, so every setting counts and nothing is missed.
    private static bool Same(LabelObject a, LabelObject b) =>
        System.Text.Json.JsonSerializer.Serialize(a, Serialization.ProjectJson.Default) ==
        System.Text.Json.JsonSerializer.Serialize(b, Serialization.ProjectJson.Default);

    /// <summary>Swaps an object on a page for its restyled copy, in the same place in the stacking order.</summary>
    private sealed class ReplaceObjectCommand : IUndoableCommand
    {
        private readonly LabelDocument _page;
        private readonly LabelObject _before;
        private readonly LabelObject _after;

        public ReplaceObjectCommand(LabelDocument page, LabelObject before, LabelObject after)
        {
            _page = page;
            _before = before;
            _after = after;
        }

        public string Label => "Apply page format";

        public void Do() => Swap(_before, _after);

        public void Undo() => Swap(_after, _before);

        private void Swap(LabelObject from, LabelObject to)
        {
            int index = _page.Objects.IndexOf(from);
            if (index >= 0)
            {
                _page.Objects[index] = to;
            }
        }
    }
}
