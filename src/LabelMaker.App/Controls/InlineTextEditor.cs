using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using LabelMaker.App.Services;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Units;
using CoreParagraph = LabelMaker.Core.Model.Text.Paragraph;
using CoreRichText = LabelMaker.Core.Model.Text.RichTextContent;
using CoreRun = LabelMaker.Core.Model.Text.TextRun;
using HAlign = LabelMaker.Core.Model.Text.HorizontalAlignment;
using Thickness = System.Windows.Thickness;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace LabelMaker.App.Controls;

/// <summary>
/// Edits a text box directly on the canvas. A RichTextBox is laid exactly over the
/// object at the current zoom (and rotation); Ctrl+B / Ctrl+I / Ctrl+U and the
/// floating toolbar format just the selected words. Committing converts the flow
/// document back into the label's own rich text as a single undo step, storing a
/// per-word value only where it differs from the box's defaults — so later
/// box-wide changes still flow through untouched words.
/// </summary>
public sealed class InlineTextEditor
{
    private const double PtToMm = UnitConverter.MmPerInch / UnitConverter.PointsPerInch;

    private readonly DesignerCanvas _canvas;
    private readonly Canvas _overlay;
    private readonly Func<DocumentSession?> _session;

    private TextObject? _target;
    private RichTextBox? _box;
    private Border? _toolbar;
    private double _scale;
    private bool _closing;

    public InlineTextEditor(DesignerCanvas canvas, Canvas overlay, Func<DocumentSession?> session)
    {
        _canvas = canvas;
        _overlay = overlay;
        _session = session;

        _canvas.EditTextRequested += (_, obj) => { if (obj is TextObject t) Begin(t); };
        _canvas.ZoomChanged += (_, _) => Commit();
        _canvas.ViewChanged += (_, _) => Commit();
    }

    public bool IsEditing => _target is not null;

    private Thickness _basePadding;
    private double _boxHeight;

    private double _boxTop;
    private static readonly LabelMaker.App.Rendering.WpfTextMeasurer Measurer = new();

    /// <summary>
    /// Places the typed text where the label draws it. The label aligns on the letters (capital
    /// tops, descenders), not on the font's empty space above them, so the editor is lifted by
    /// that space and its top padding takes up the rest of the offset.
    /// </summary>
    private void AlignVertically()
    {
        if (_box is null || _target is null)
        {
            return;
        }

        string plain = new TextRange(_box.Document.ContentStart, _box.Document.ContentEnd).Text.TrimEnd('\r', '\n');
        double innerMm = Math.Max(0.5, _target.Bounds.Height - _target.PaddingMm.Vertical);
        var contentMm = new RectMm(0, 0, Math.Max(0.5, _target.Bounds.Width - _target.PaddingMm.Horizontal), innerMm);
        Core.Rendering.TextBlock block = Core.Rendering.TextLayouter.Layout(
            CoreRichText.FromPlainText(plain.Length == 0 ? " " : plain), _target, contentMm, Measurer);

        double lift = block.CapTopMm * _scale;
        double offset = block.TopOffsetMm(_target.VerticalAlignment, innerMm) * _scale;
        var padding = new Thickness(_basePadding.Left, Math.Max(0, _basePadding.Top + offset + lift), _basePadding.Right, _basePadding.Bottom);
        if (Math.Abs(_box.Padding.Top - padding.Top) > 0.5)
        {
            _box.Padding = padding;
        }

        Canvas.SetTop(_box, _boxTop - lift);
        _box.MinHeight = Math.Max(16, _boxHeight + lift);
    }

    // ---- lifecycle --------------------------------------------------------

    public void Begin(TextObject target)
    {
        Commit();
        _target = target;
        _scale = _canvas.ScreenScale;

        Rect rect = _canvas.ObjectScreenRect(target);
        Thickness pad = new(
            target.PaddingMm.Left * _scale, target.PaddingMm.Top * _scale,
            target.PaddingMm.Right * _scale, target.PaddingMm.Bottom * _scale);

        _box = new RichTextBox
        {
            Width = Math.Max(24, rect.Width),
            MinHeight = Math.Max(16, rect.Height),
            Padding = pad,
            BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2C, 0x6F, 0xE8)),
            Background = target.Background.IsTransparent
                ? new SolidColorBrush(Color.FromArgb(235, 255, 255, 255))
                : ToBrush(target.Background),
            AcceptsReturn = true,
            AcceptsTab = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FlowDirection = target.Direction == Core.Model.Text.TextDirection.RightToLeft
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight,
            Document = ToFlowDocument(target, _scale),
            SpellCheck = { IsEnabled = false },
        };

        _box.Document.PagePadding = new Thickness(0);
        if (!target.WordWrap)
        {
            _box.Document.PageWidth = 100000;
        }

        if (Math.Abs(target.RotationDegrees) > 0.01)
        {
            _box.RenderTransformOrigin = new Point(0.5, 0.5);
            _box.RenderTransform = new RotateTransform(target.RotationDegrees);
        }

        Canvas.SetLeft(_box, rect.X);
        Canvas.SetTop(_box, rect.Y);

        _box.PreviewKeyDown += OnKeyDown;
        _box.LostKeyboardFocus += OnLostFocus;

        // Middle and bottom aligned text stays where it is drawn on the label while typing,
        // instead of jumping to the top of the box as the editor opens.
        _basePadding = pad;
        _boxHeight = rect.Height;
        _boxTop = rect.Y;
        _box.Loaded += (_, _) => _box.Dispatcher.InvokeAsync(AlignVertically, System.Windows.Threading.DispatcherPriority.Loaded);
        _box.TextChanged += (_, _) => AlignVertically();

        _toolbar = BuildToolbar();
        Canvas.SetLeft(_toolbar, rect.X);
        Canvas.SetTop(_toolbar, Math.Max(0, rect.Y - 40));

        _overlay.Children.Add(_box);
        _overlay.Children.Add(_toolbar);
        _canvas.HiddenObjectId = target.Id;

        _box.Focus();
        _box.SelectAll();
    }

    /// <summary>Applies the edit (if anything changed) and closes the editor.</summary>
    public void Commit() => End(apply: true);

    public void Cancel() => End(apply: false);

    private void End(bool apply)
    {
        if (_closing || _target is null || _box is null)
        {
            return;
        }

        _closing = true;
        try
        {
            TextObject target = _target;
            if (apply && _session() is { } session)
            {
                CoreRichText next = FromFlowDocument(_box.Document, target, _scale);
                if (!SameContent(next, target.Content))
                {
                    session.Execute(new SetPropertyCommand<CoreRichText>(
                        "Edit text", () => target.Content, v => target.Content = v, next));
                }
            }

            _box.PreviewKeyDown -= OnKeyDown;
            _box.LostKeyboardFocus -= OnLostFocus;
            _overlay.Children.Remove(_box);
            if (_toolbar is not null)
            {
                _overlay.Children.Remove(_toolbar);
            }

            _box = null;
            _toolbar = null;
            _target = null;
            _canvas.HiddenObjectId = null;
            _canvas.Focus();
        }
        finally
        {
            _closing = false;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Commit();
            e.Handled = true;
        }
    }

    private void OnLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Clicking the floating toolbar must not end the edit.
        if (e.NewFocus is DependencyObject d && _toolbar is not null && IsInside(d, _toolbar))
        {
            return;
        }

        _box?.Dispatcher.BeginInvoke(() =>
        {
            if (_box is not null && !_box.IsKeyboardFocusWithin &&
                !(_toolbar?.IsKeyboardFocusWithin ?? false) && !(_toolbar?.IsMouseOver ?? false))
            {
                Commit();
            }
        });
    }

    private static bool IsInside(DependencyObject d, DependencyObject container)
    {
        for (DependencyObject? cur = d; cur is not null; cur = VisualTreeHelper.GetParent(cur) ?? LogicalTreeHelper.GetParent(cur))
        {
            if (ReferenceEquals(cur, container))
            {
                return true;
            }
        }

        return false;
    }

    // ---- floating toolbar -------------------------------------------------

    private Border BuildToolbar()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        Button Tool(string content, string tip, Action action, FontWeight? weight = null, FontStyle? style = null,
            TextDecorationCollection? decorations = null)
        {
            var text = new TextBlock { Text = content, FontSize = 13 };
            if (weight is { } w)
            {
                text.FontWeight = w;
            }

            if (style is { } s)
            {
                text.FontStyle = s;
            }

            if (decorations is not null)
            {
                text.TextDecorations = decorations;
            }

            var button = new Button
            {
                Content = text,
                ToolTip = tip,
                Width = 30,
                Height = 28,
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false,
                Style = (Style)Application.Current.FindResource("IconGhost"),
            };
            button.Click += (_, _) => { action(); _box?.Focus(); };
            return button;
        }

        panel.Children.Add(Tool("B", "Bold (Ctrl+B)", () => EditingCommands.ToggleBold.Execute(null, _box), weight: FontWeights.Bold));
        panel.Children.Add(Tool("I", "Italic (Ctrl+I)", () => EditingCommands.ToggleItalic.Execute(null, _box), style: FontStyles.Italic));
        panel.Children.Add(Tool("U", "Underline (Ctrl+U)", () => EditingCommands.ToggleUnderline.Execute(null, _box), decorations: TextDecorations.Underline));
        panel.Children.Add(Tool("S", "Strikethrough", ToggleStrike, decorations: TextDecorations.Strikethrough));
        panel.Children.Add(Separator());
        panel.Children.Add(Tool("A−", "Smaller", () => ChangeSize(1 / 1.15)));
        panel.Children.Add(Tool("A+", "Larger", () => ChangeSize(1.15)));
        panel.Children.Add(Separator());

        foreach (string hex in new[] { "#000000", "#E0555F", "#2C6FE8", "#22B268", "#F5A524", "#7C4DFF" })
        {
            if (!ColorRgba.TryParse(hex, out ColorRgba c))
            {
                continue;
            }

            var swatch = new Button
            {
                Width = 18,
                Height = 18,
                Margin = new Thickness(3, 0, 3, 0),
                Background = ToBrush(c),
                BorderBrush = (Brush)Application.Current.FindResource("Br.BorderStrong"),
                BorderThickness = new Thickness(1),
                ToolTip = $"Colour {hex}",
                Focusable = false,
                Cursor = Cursors.Hand,
            };
            swatch.Template = SwatchTemplate();
            swatch.Click += (_, _) =>
            {
                _box?.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, ToBrush(c));
                _box?.Focus();
            };
            panel.Children.Add(swatch);
        }

        panel.Children.Add(Separator());
        panel.Children.Add(Tool("✓", "Done (Ctrl+Enter)", Commit));

        return new Border
        {
            Child = panel,
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(8),
            Background = (Brush)Application.Current.FindResource("Br.Bg"),
            BorderBrush = (Brush)Application.Current.FindResource("Br.BorderStrong"),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 12, ShadowDepth = 2, Direction = 270, Opacity = 0.2,
            },
        };
    }

    private static UIElement Separator() => new Rectangle
    {
        Width = 1,
        Margin = new Thickness(4, 4, 4, 4),
        Fill = (Brush)Application.Current.FindResource("Br.Border"),
    };

    private static ControlTemplate SwatchTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    private void ToggleStrike()
    {
        if (_box is null)
        {
            return;
        }

        object current = _box.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
        bool on = current is TextDecorationCollection tdc && tdc.Any(d => d.Location == TextDecorationLocation.Strikethrough);
        _box.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, on ? new TextDecorationCollection() : TextDecorations.Strikethrough);
    }

    private void ChangeSize(double factor)
    {
        if (_box is null)
        {
            return;
        }

        object current = _box.Selection.GetPropertyValue(TextElement.FontSizeProperty);
        double size = current is double d ? d : _box.FontSize;
        _box.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, Math.Clamp(size * factor, 4, 2000));
    }

    // ---- conversion -------------------------------------------------------

    /// <summary>Label rich text to a WPF flow document sized for the canvas at <paramref name="scale"/> DIU/mm.</summary>
    public static FlowDocument ToFlowDocument(TextObject obj, double scale)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily(obj.FontFamily),
            FontSize = PtToPx(obj.FontSizePt, scale),
            Foreground = ToBrush(obj.Foreground),
            TextAlignment = ToWpf(obj.HorizontalAlignment),
            LineHeight = double.NaN,
        };

        foreach (CoreParagraph p in obj.Content.Paragraphs)
        {
            var para = new WpfParagraph
            {
                Margin = new Thickness(0),
                TextAlignment = ToWpf(p.Alignment ?? obj.HorizontalAlignment),
            };

            if (obj.LineSpacing is > 0 and not 1.0)
            {
                para.LineHeight = PtToPx((p.Runs.FirstOrDefault()?.FontSizePt ?? obj.FontSizePt) * 1.2 * obj.LineSpacing, scale);
            }

            foreach (CoreRun r in p.Runs)
            {
                var run = new Run(r.Text)
                {
                    FontFamily = new FontFamily(r.FontFamily ?? obj.FontFamily),
                    FontSize = PtToPx(r.FontSizePt ?? obj.FontSizePt, scale),
                    FontWeight = (r.Bold ?? obj.Bold) ? FontWeights.Bold : FontWeights.Normal,
                    FontStyle = (r.Italic ?? obj.Italic) ? FontStyles.Italic : FontStyles.Normal,
                    Foreground = ToBrush(r.Foreground ?? obj.Foreground),
                };

                ColorRgba background = r.Background ?? obj.Background;
                if (!background.IsTransparent)
                {
                    run.Background = ToBrush(background);
                }

                var decorations = new TextDecorationCollection();
                if (r.Underline ?? obj.Underline)
                {
                    decorations.Add(TextDecorations.Underline);
                }

                if (r.Strikethrough ?? obj.Strikethrough)
                {
                    decorations.Add(TextDecorations.Strikethrough);
                }

                run.TextDecorations = decorations;
                para.Inlines.Add(run);
            }

            doc.Blocks.Add(para);
        }

        if (doc.Blocks.Count == 0)
        {
            doc.Blocks.Add(new WpfParagraph { Margin = new Thickness(0) });
        }

        return doc;
    }

    /// <summary>
    /// Flow document back to label rich text. Values equal to the box defaults are
    /// stored as "inherit" (null), so the box-level properties stay in charge.
    /// </summary>
    public static CoreRichText FromFlowDocument(FlowDocument doc, TextObject obj, double scale)
    {
        var content = new CoreRichText();

        foreach (Block block in doc.Blocks)
        {
            if (block is not WpfParagraph para)
            {
                continue;
            }

            CoreParagraph current = NewParagraph(para, obj);
            content.Paragraphs.Add(current);

            foreach (Inline inline in FlattenInlines(para.Inlines))
            {
                if (inline is LineBreak)
                {
                    // Shift+Enter: the label model has paragraphs only, so start a new one.
                    current = NewParagraph(para, obj);
                    content.Paragraphs.Add(current);
                    continue;
                }

                if (inline is not Run run || run.Text.Length == 0)
                {
                    continue;
                }

                current.Runs.Add(ToCoreRun(run, obj, scale));
            }
        }

        if (content.Paragraphs.Count == 0)
        {
            content.Paragraphs.Add(new CoreParagraph());
        }

        foreach (CoreParagraph p in content.Paragraphs.Where(p => p.Runs.Count == 0))
        {
            p.Runs.Add(new CoreRun());
        }

        return content;
    }

    private static CoreParagraph NewParagraph(WpfParagraph para, TextObject obj)
    {
        HAlign align = FromWpf(para.TextAlignment);
        return new CoreParagraph { Alignment = align == obj.HorizontalAlignment ? null : align };
    }

    /// <summary>Runs and line breaks in reading order, unwrapping Bold/Italic/Span containers.</summary>
    private static IEnumerable<Inline> FlattenInlines(InlineCollection inlines)
    {
        foreach (Inline inline in inlines)
        {
            if (inline is Span span)
            {
                foreach (Inline child in FlattenInlines(span.Inlines))
                {
                    yield return child;
                }
            }
            else
            {
                yield return inline;
            }
        }
    }

    private static CoreRun ToCoreRun(Run run, TextObject obj, double scale)
    {
        // Run's properties are inherited, so reading them gives the effective value
        // even when formatting came from a surrounding Bold/Span element.
        string family = run.FontFamily.Source;
        double pt = Math.Round(PxToPt(run.FontSize, scale) * 2, MidpointRounding.AwayFromZero) / 2;
        bool bold = run.FontWeight.ToOpenTypeWeight() >= FontWeights.SemiBold.ToOpenTypeWeight();
        bool italic = run.FontStyle != FontStyles.Normal;

        TextDecorationCollection decorations = CollectDecorations(run);
        bool underline = decorations.Any(d => d.Location == TextDecorationLocation.Underline);
        bool strike = decorations.Any(d => d.Location == TextDecorationLocation.Strikethrough);

        ColorRgba foreground = run.Foreground is SolidColorBrush fg ? FromWpf(fg.Color) : obj.Foreground;
        ColorRgba background = run.Background is SolidColorBrush bg ? FromWpf(bg.Color) : obj.Background;

        return new CoreRun
        {
            Text = run.Text,
            FontFamily = family == obj.FontFamily ? null : family,
            FontSizePt = Math.Abs(pt - obj.FontSizePt) < 0.26 ? null : pt,
            Bold = bold == obj.Bold ? null : bold,
            Italic = italic == obj.Italic ? null : italic,
            Underline = underline == obj.Underline ? null : underline,
            Strikethrough = strike == obj.Strikethrough ? null : strike,
            Foreground = foreground == obj.Foreground ? null : foreground,
            Background = background == obj.Background || background.IsTransparent && obj.Background.IsTransparent ? null : background,
        };
    }

    /// <summary>TextDecorations are not inherited, so collect them from the run and every container above it.</summary>
    private static TextDecorationCollection CollectDecorations(Inline inline)
    {
        var all = new TextDecorationCollection();
        for (DependencyObject? cur = inline; cur is Inline i; cur = i.Parent)
        {
            if (i.TextDecorations is { } tdc)
            {
                foreach (TextDecoration d in tdc)
                {
                    all.Add(d);
                }
            }
        }

        return all;
    }

    private static bool SameContent(CoreRichText a, CoreRichText b)
    {
        if (a.Paragraphs.Count != b.Paragraphs.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Paragraphs.Count; i++)
        {
            CoreParagraph pa = a.Paragraphs[i];
            CoreParagraph pb = b.Paragraphs[i];
            if (pa.Alignment != pb.Alignment)
            {
                return false;
            }

            var ra = pa.Runs.Where(r => r.Text.Length > 0).ToList();
            var rb = pb.Runs.Where(r => r.Text.Length > 0).ToList();
            if (ra.Count != rb.Count || pa.PlainText != pb.PlainText)
            {
                return false;
            }

            for (int j = 0; j < ra.Count; j++)
            {
                CoreRun x = ra[j], y = rb[j];
                if (x.Text != y.Text || x.Bold != y.Bold || x.Italic != y.Italic || x.Underline != y.Underline ||
                    x.Strikethrough != y.Strikethrough || x.FontFamily != y.FontFamily || x.FontSizePt != y.FontSizePt ||
                    x.Foreground != y.Foreground || x.Background != y.Background)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static double PtToPx(double pt, double scale) => Math.Max(1, pt * PtToMm * scale);

    private static double PxToPt(double px, double scale) => px / scale / PtToMm;

    private static SolidColorBrush ToBrush(ColorRgba c) => new(Color.FromArgb(c.A, c.R, c.G, c.B));

    private static ColorRgba FromWpf(Color c) => new(c.R, c.G, c.B, c.A);

    private static TextAlignment ToWpf(HAlign a) => a switch
    {
        HAlign.Center => TextAlignment.Center,
        HAlign.Right => TextAlignment.Right,
        HAlign.Justify => TextAlignment.Justify,
        _ => TextAlignment.Left,
    };

    private static HAlign FromWpf(TextAlignment a) => a switch
    {
        TextAlignment.Center => HAlign.Center,
        TextAlignment.Right => HAlign.Right,
        TextAlignment.Justify => HAlign.Justify,
        _ => HAlign.Left,
    };
}
