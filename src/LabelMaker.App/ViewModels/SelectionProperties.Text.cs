using System.Collections.ObjectModel;
using System.Windows.Media;
using LabelMaker.App.Mvvm;
using LabelMaker.App.Services;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Units;

namespace LabelMaker.App.ViewModels;

/// <summary>The Properties panel for a text box: words, font, colours, alignment and fitting.</summary>
public sealed partial class SelectionProperties
{
    /// <summary>
    /// Plain-text view of the box. Edits are spliced into the existing rich text,
    /// so a bold word or a coloured price survives typing in this field.
    /// </summary>
    public string TextContent
    {
        get => (Obj as TextObject)?.Content.PlainText ?? string.Empty;
        set
        {
            if (Obj is TextObject t && t.Content.PlainText != (value ?? string.Empty).Replace("\r\n", "\n"))
            {
                Set(nameof(TextContent), () => t.Content, c => t.Content = c,
                    RichTextEditing.ApplyPlainText(t.Content, value ?? string.Empty));
            }
        }
    }

    /// <summary>True when some words carry their own formatting (set in the on-canvas editor).</summary>
    public bool HasMixedFormatting =>
        Obj is TextObject t && t.Content.Paragraphs.SelectMany(p => p.Runs).Any(r =>
            r.Bold is not null || r.Italic is not null || r.Underline is not null || r.Strikethrough is not null ||
            r.FontFamily is not null || r.FontSizePt is not null || r.Foreground is not null || r.Background is not null);

    public double FontSizePt
    {
        get => (Obj as TextObject)?.FontSizePt ?? 10;
        set => SetTextStyle(nameof(FontSizePt), t => t.FontSizePt, (t, v) => t.FontSizePt = v, Math.Clamp(value, 2, 400),
            r => r.FontSizePt = null);
    }

    public string FontFamily
    {
        get => (Obj as TextObject)?.FontFamily ?? "Segoe UI";
        set => SetTextStyle(nameof(FontFamily), t => t.FontFamily, (t, v) => t.FontFamily = v, value,
            r => r.FontFamily = null);
    }

    public bool Bold
    {
        get => (Obj as TextObject)?.Bold ?? false;
        set => SetTextStyle(nameof(Bold), t => t.Bold, (t, v) => t.Bold = v, value, r => r.Bold = null);
    }

    public bool Italic
    {
        get => (Obj as TextObject)?.Italic ?? false;
        set => SetTextStyle(nameof(Italic), t => t.Italic, (t, v) => t.Italic = v, value, r => r.Italic = null);
    }

    public bool Underline
    {
        get => (Obj as TextObject)?.Underline ?? false;
        set => SetTextStyle(nameof(Underline), t => t.Underline, (t, v) => t.Underline = v, value, r => r.Underline = null);
    }

    public bool Strikethrough
    {
        get => (Obj as TextObject)?.Strikethrough ?? false;
        set => SetTextStyle(nameof(Strikethrough), t => t.Strikethrough, (t, v) => t.Strikethrough = v, value,
            r => r.Strikethrough = null);
    }

    public string TextColorHex
    {
        get => ((Obj as TextObject)?.Foreground ?? ColorRgba.Black).ToHex();
        set
        {
            if (ColorRgba.TryParse(value, out ColorRgba c))
            {
                SetTextStyle(nameof(TextColorHex), t => t.Foreground, (t, v) => t.Foreground = v, c, r => r.Foreground = null);
            }
        }
    }

    public string HighlightHex
    {
        get => ((Obj as TextObject)?.Background ?? ColorRgba.Transparent).ToHex();
        set
        {
            if (ColorRgba.TryParse(value, out ColorRgba c))
            {
                if (Obj is TextObject { BackgroundTo: not null } withGradient)
                {
                    // A single colour typed or picked here replaces the gradient.
                    ApplyFill("highlight", new FillChoice(c, null, withGradient.BackgroundAngleDegrees));
                    return;
                }

                SetTextStyle(nameof(HighlightHex), t => t.Background, (t, v) => t.Background = v, c, r => r.Background = null);
            }
        }
    }

    public Brush TextColorBrush => ToBrush((Obj as TextObject)?.Foreground ?? ColorRgba.Black);

    public Brush HighlightBrush => Obj is TextObject { BackgroundTo: { } to } text
        ? ToBrush(text.Background, to, text.BackgroundAngleDegrees)
        : ToBrush((Obj as TextObject)?.Background ?? ColorRgba.Transparent);

    public static IReadOnlyList<HorizontalAlignment> HorizontalAlignments { get; } = Enum.GetValues<HorizontalAlignment>();

    public static IReadOnlyList<VerticalAlignment> VerticalAlignments { get; } = Enum.GetValues<VerticalAlignment>();

    public static IReadOnlyList<KeyValuePair<TextAutoFit, string>> AutoFitModes { get; } = new[]
    {
        new KeyValuePair<TextAutoFit, string>(TextAutoFit.ResizeBox, "Grow the box"),
        new KeyValuePair<TextAutoFit, string>(TextAutoFit.ShrinkToFit, "Shrink the text"),
        new KeyValuePair<TextAutoFit, string>(TextAutoFit.None, "Let it spill out"),
    };

    public static IReadOnlyList<TextDirection> TextDirections { get; } = Enum.GetValues<TextDirection>();

    /// <summary>Box alignment. Setting it also clears per-paragraph overrides so the whole box follows.</summary>
    public HorizontalAlignment TextAlign
    {
        get => (Obj as TextObject)?.HorizontalAlignment ?? HorizontalAlignment.Left;
        set
        {
            if (Obj is not TextObject t)
            {
                return;
            }

            RichTextContent content = t.Content.Clone();
            foreach (Paragraph p in content.Paragraphs)
            {
                p.Alignment = null;
            }

            Execute("Text alignment", new IUndoableCommand[]
            {
                new SetPropertyCommand<HorizontalAlignment>("Align", () => t.HorizontalAlignment, v => t.HorizontalAlignment = v, value),
                new SetPropertyCommand<RichTextContent>("Align", () => t.Content, v => t.Content = v, content),
            });
        }
    }

    public VerticalAlignment TextVerticalAlign
    {
        get => (Obj as TextObject)?.VerticalAlignment ?? VerticalAlignment.Top;
        set
        {
            if (Obj is TextObject t)
            {
                Set(nameof(TextVerticalAlign), () => t.VerticalAlignment, v => t.VerticalAlignment = v, value);
            }
        }
    }

    public TextDirection TextDirectionValue
    {
        get => (Obj as TextObject)?.Direction ?? TextDirection.LeftToRight;
        set
        {
            if (Obj is TextObject t)
            {
                Set(nameof(TextDirectionValue), () => t.Direction, v => t.Direction = v, value);
            }
        }
    }

    public bool WordWrap
    {
        get => (Obj as TextObject)?.WordWrap ?? true;
        set
        {
            if (Obj is TextObject t)
            {
                Set(nameof(WordWrap), () => t.WordWrap, v => t.WordWrap = v, value);
            }
        }
    }

    public TextAutoFit AutoFit
    {
        get => (Obj as TextObject)?.AutoFit ?? TextAutoFit.None;
        set
        {
            if (Obj is TextObject t)
            {
                Set(nameof(AutoFit), () => t.AutoFit, v => t.AutoFit = v, value);
                OnPropertyChanged(nameof(IsShrinkToFit));
            }
        }
    }

    public bool IsShrinkToFit => (Obj as TextObject)?.AutoFit == TextAutoFit.ShrinkToFit;

    public double MinAutoFitPt
    {
        get => (Obj as TextObject)?.MinAutoFitPt ?? 4;
        set
        {
            if (Obj is TextObject t)
            {
                Set(nameof(MinAutoFitPt), () => t.MinAutoFitPt, v => t.MinAutoFitPt = v, Math.Clamp(value, 2, 200));
            }
        }
    }

    public double LineSpacing
    {
        get => Round((Obj as TextObject)?.LineSpacing ?? 1);
        set
        {
            if (Obj is TextObject t)
            {
                Set(nameof(LineSpacing), () => t.LineSpacing, v => t.LineSpacing = v, Math.Round(Math.Clamp(value, 0.3, 10), 2));
            }
        }
    }

    /// <summary>Letter spacing in em (0 = normal).</summary>
    public double LetterSpacingEm
    {
        get => Round((Obj as TextObject)?.LetterSpacingEm ?? 0);
        set
        {
            if (Obj is TextObject t)
            {
                Set(nameof(LetterSpacingEm), () => t.LetterSpacingEm, v => t.LetterSpacingEm = v, Math.Round(Math.Clamp(value, -0.5, 5), 3));
            }
        }
    }

    /// <summary>Uniform inner padding, in the display unit.</summary>
    public double TextPadding
    {
        get => Out((Obj as TextObject)?.PaddingMm.Top ?? 0.5);
        set
        {
            if (Obj is TextObject t)
            {
                double mm = Math.Clamp(In(value), 0, 50);
                Set(nameof(TextPadding), () => t.PaddingMm, v => t.PaddingMm = v, new LabelMaker.Core.Model.Objects.Thickness(mm));
            }
        }
    }

    /// <summary>
    /// Sets an object-level text style and, in the same undo step, removes the
    /// matching per-word overrides — otherwise ticking Bold would do nothing to
    /// words that were individually un-bolded in the canvas editor.
    /// </summary>
    private void SetTextStyle<T>(string name, Func<TextObject, T> get, Action<TextObject, T> set, T value, Action<TextRun> clearOverride)
    {
        if (Obj is not TextObject t)
        {
            return;
        }

        RichTextContent content = t.Content.Clone();
        bool hadOverrides = false;
        foreach (TextRun run in content.Paragraphs.SelectMany(p => p.Runs))
        {
            TextRun before = run.Clone();
            clearOverride(run);
            hadOverrides |= !SameRunStyle(before, run);
        }

        if (EqualityComparer<T>.Default.Equals(get(t), value) && !hadOverrides)
        {
            return;
        }

        var commands = new List<IUndoableCommand>
        {
            // The merge key folds a slider drag into one undo step.
            new SetPropertyCommand<T>($"Edit {name}", () => get(t), v => set(t, v), value, (t.Id, name)),
        };

        if (hadOverrides)
        {
            commands.Add(new SetPropertyCommand<RichTextContent>($"Edit {name}", () => t.Content, v => t.Content = v, content));
        }

        Execute($"Edit {name}", commands);
    }

    private static bool SameRunStyle(TextRun a, TextRun b) =>
        a.Bold == b.Bold && a.Italic == b.Italic && a.Underline == b.Underline && a.Strikethrough == b.Strikethrough &&
        a.FontFamily == b.FontFamily && a.FontSizePt == b.FontSizePt && a.Foreground == b.Foreground && a.Background == b.Background;
}
