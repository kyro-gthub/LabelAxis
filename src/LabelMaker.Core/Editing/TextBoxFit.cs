using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;

namespace LabelMaker.Core.Editing;

/// <summary>
/// Keeps text boxes set to <see cref="TextAutoFit.ResizeBox"/> at least as tall as their text,
/// so the box on the canvas always encloses what it shows.
/// </summary>
public static class TextBoxFit
{
    /// <summary>The height <paramref name="text"/> needs at its current width, padding included.</summary>
    public static double RequiredHeightMm(TextObject text, ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measurer);
        RectMm content = new(0, 0, Math.Max(0.5, text.Bounds.Width - text.PaddingMm.Horizontal), 1000);
        TextBlock block = TextLayouter.Layout(text.Content, text, content, measurer);
        return block.InkHeightMm + text.PaddingMm.Vertical;
    }

    /// <summary>
    /// Runs <paramref name="command"/>; if that left any grow-to-fit text box in
    /// <paramref name="document"/> shorter than its text, the box is made taller as part of the
    /// same undo step. Returns the command to record (the original when nothing grew).
    /// </summary>
    public static IUndoableCommand ExecuteWithFit(IUndoableCommand command, LabelDocument document, ITextMeasurer measurer) =>
        ExecuteWithFit(command, document, measurer, resolve: null);

    /// <summary>
    /// As above, and natural-size barcodes and their boxes are fitted together too (see
    /// <see cref="BarcodeFit"/>); <paramref name="resolve"/> fills in data fields in barcode values.
    /// </summary>
    public static IUndoableCommand ExecuteWithFit(IUndoableCommand command, LabelDocument document, ITextMeasurer measurer, Func<string, string>? resolve)
    {
        ArgumentNullException.ThrowIfNull(command);
        Dictionary<BarcodeObject, (RectMm Bounds, string Settings)> barcodes = BarcodeFit.Snapshot(document);
        command.Do();
        List<(LabelObject Object, RectMm Before, RectMm After)> grown = Grow(document, measurer);
        var fitted = BarcodeFit.Fit(document, barcodes, resolve);
        return grown.Count == 0 && fitted.Count == 0 ? command : new FitTextCommand(command, grown, fitted);
    }

    /// <summary>Makes every grow-to-fit box in <paramref name="document"/> tall enough, returning what changed.</summary>
    public static List<(LabelObject Object, RectMm Before, RectMm After)> Grow(LabelDocument? document, ITextMeasurer measurer)
    {
        var changed = new List<(LabelObject, RectMm, RectMm)>();
        if (document is null)
        {
            return changed;
        }

        foreach (TextObject text in document.Objects.OfType<TextObject>().Where(t => t.AutoFit == TextAutoFit.ResizeBox))
        {
            double needed = Math.Round(RequiredHeightMm(text, measurer), 2);
            if (needed > text.Bounds.Height + 0.01)
            {
                RectMm before = text.Bounds;
                RectMm after = before with { Height = needed };
                text.Bounds = after;
                changed.Add((text, before, after));
            }
        }

        return changed;
    }

    /// <summary>An edit plus the box heights it made grow and the barcodes it fitted; undone together.</summary>
    private sealed class FitTextCommand : IUndoableCommand
    {
        private readonly IUndoableCommand _inner;
        private readonly List<(LabelObject Object, RectMm Before, RectMm After)> _grown;
        private readonly List<(BarcodeObject Barcode, RectMm BoundsBefore, RectMm BoundsAfter, BarcodeSpec SpecBefore, BarcodeSpec SpecAfter)> _fitted;

        public FitTextCommand(IUndoableCommand inner, List<(LabelObject, RectMm, RectMm)> grown,
            List<(BarcodeObject, RectMm, RectMm, BarcodeSpec, BarcodeSpec)> fitted)
        {
            _inner = inner;
            _grown = grown;
            _fitted = fitted;
        }

        public string Label => _inner.Label;

        public void Do()
        {
            _inner.Do();
            foreach ((LabelObject obj, _, RectMm after) in _grown)
            {
                obj.Bounds = after;
            }

            foreach ((BarcodeObject barcode, _, RectMm bounds, _, BarcodeSpec spec) in _fitted)
            {
                barcode.Spec = spec;
                barcode.Bounds = bounds;
            }
        }

        public void Undo()
        {
            for (int i = _fitted.Count - 1; i >= 0; i--)
            {
                _fitted[i].Barcode.Spec = _fitted[i].SpecBefore;
                _fitted[i].Barcode.Bounds = _fitted[i].BoundsBefore;
            }

            for (int i = _grown.Count - 1; i >= 0; i--)
            {
                _grown[i].Object.Bounds = _grown[i].Before;
            }

            _inner.Undo();
        }

        // Slider drags and typing merge into one undo step; the grown heights merge with them,
        // keeping each box's first "before" and latest "after".
        public bool TryMergeWith(IUndoableCommand next)
        {
            IUndoableCommand nextInner = next is FitTextCommand fit ? fit._inner : next;
            if (!_inner.TryMergeWith(nextInner))
            {
                return false;
            }

            if (next is FitTextCommand other)
            {
                foreach (var f in other._fitted)
                {
                    int at = _fitted.FindIndex(g => ReferenceEquals(g.Barcode, f.Barcode));
                    if (at >= 0)
                    {
                        _fitted[at] = (f.Barcode, _fitted[at].BoundsBefore, f.BoundsAfter, _fitted[at].SpecBefore, f.SpecAfter);
                    }
                    else
                    {
                        _fitted.Add(f);
                    }
                }

                foreach ((LabelObject obj, RectMm before, RectMm after) in other._grown)
                {
                    int index = _grown.FindIndex(g => ReferenceEquals(g.Object, obj));
                    if (index >= 0)
                    {
                        _grown[index] = (obj, _grown[index].Before, after);
                    }
                    else
                    {
                        _grown.Add((obj, before, after));
                    }
                }
            }

            return true;
        }
    }
}
