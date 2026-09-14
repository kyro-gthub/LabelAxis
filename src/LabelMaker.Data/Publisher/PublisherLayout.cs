using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Serialization;
using LabelMaker.Core.Units;
using HAlign = LabelMaker.Core.Model.Text.HorizontalAlignment;
using VAlign = LabelMaker.Core.Model.Text.VerticalAlignment;

namespace LabelMaker.Data.Publisher;

public enum PubShapeKind
{
    Text,
    Picture,
    Rectangle,
    RoundRectangle,
    Ellipse,
    Line,
}

public sealed record PubRun(string Text, string FontName, double SizePt, bool Bold, bool Italic, bool Underline, ColorRgba Color);

public sealed record PubParagraph(IReadOnlyList<PubRun> Runs, HAlign Alignment);

/// <summary>
/// One shape on a Publisher page, positions and sizes in points from the page's top-left
/// corner, as Publisher reports them.
/// </summary>
public sealed record PubShape
{
    public PubShapeKind Kind { get; init; }

    public string Name { get; init; } = string.Empty;

    public double LeftPt { get; init; }

    public double TopPt { get; init; }

    public double WidthPt { get; init; }

    public double HeightPt { get; init; }

    public double RotationDegrees { get; init; }

    /// <summary>Fill colour, or null for no fill.</summary>
    public ColorRgba? Fill { get; init; }

    /// <summary>Outline colour, or null for no outline.</summary>
    public ColorRgba? Line { get; init; }

    public double LineWeightPt { get; init; } = 0.75;

    public IReadOnlyList<PubParagraph> Paragraphs { get; init; } = Array.Empty<PubParagraph>();

    public VAlign VerticalAlignment { get; init; } = VAlign.Top;

    /// <summary>Text insets (left, top, right, bottom) in points.</summary>
    public (double L, double T, double R, double B) InsetsPt { get; init; } = (7.2, 3.6, 7.2, 3.6);

    public byte[]? ImageBytes { get; init; }

    public string ImageExtension { get; init; } = ".png";

    /// <summary>Shapes that were grouped in Publisher share a group key.</summary>
    public string? GroupKey { get; init; }
}

public sealed record PubPage(string Name, IReadOnlyList<PubShape> Shapes);

/// <summary>A Publisher document as pages of positioned shapes.</summary>
public sealed record PubLayout(string Title, double PageWidthPt, double PageHeightPt, IReadOnlyList<PubPage> Pages)
{
    /// <summary>Shapes that could not be read, with the reason, to tell the user.</summary>
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Turns a <see cref="PubLayout"/> into a LabelAxis project: one page per Publisher page,
/// on a sheet the size of the Publisher page, every shape where it was.
/// </summary>
public static class PublisherLayoutConverter
{
    private const double PtToMm = 25.4 / 72.0;

    public static ProjectDocument ToProject(PubLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        double widthMm = Math.Round(layout.PageWidthPt * PtToMm, 2);
        double heightMm = Math.Round(layout.PageHeightPt * PtToMm, 2);
        LabelTemplate template = PageTemplate(widthMm, heightMm);

        ProjectModel project = ProjectModel.CreateNew(template, string.IsNullOrWhiteSpace(layout.Title) ? "Publisher import" : layout.Title);
        project.PrintSettings.Content = PrintContent.Pages;
        var document = new ProjectDocument { Project = project };

        int number = 1;
        foreach (PubPage page in layout.Pages)
        {
            var label = new LabelDocument
            {
                Name = string.IsNullOrWhiteSpace(page.Name) ? $"Page {number}" : page.Name,
                SizeMm = new SizeMm(widthMm, heightMm),
            };

            var groups = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (PubShape shape in page.Shapes)
            {
                foreach (LabelObject obj in Convert(shape, document.Assets))
                {
                    if (shape.GroupKey is { } key)
                    {
                        obj.GroupId = groups.TryGetValue(key, out string? id) ? id : groups[key] = Guid.NewGuid().ToString("N");
                    }

                    label.Objects.Add(obj);
                }
            }

            project.Pages.Add(new LabelPage { Name = label.Name, Document = label });
            number++;
        }

        document.HasUnsavedChanges = true;
        return document;
    }

    /// <summary>A one-up sheet exactly the size of the Publisher page (named A4/A5/Letter when it is one).</summary>
    public static LabelTemplate PageTemplate(double widthMm, double heightMm)
    {
        string name = Named(widthMm, heightMm) ?? "Custom";
        return new LabelTemplate
        {
            Id = $"publisher-page-{widthMm:0.#}x{heightMm:0.#}".Replace(',', '_'),
            Brand = "Publisher",
            ProductCode = $"{widthMm:0.#} × {heightMm:0.#}",
            Name = $"Publisher page {widthMm:0.#} × {heightMm:0.#} mm" + (name == "Custom" ? string.Empty : $" ({name})"),
            Categories = new List<string> { "Imported" },
            Page = new PageSize(name, widthMm, heightMm),
            Orientation = widthMm > heightMm ? PageOrientation.Landscape : PageOrientation.Portrait,
            PrintableMarginMm = MarginsMm.Zero,
            Grid = new LabelGrid { Columns = 1, Rows = 1, LabelWidthMm = widthMm, LabelHeightMm = heightMm },
            Source = "Page size of an imported Microsoft Publisher document",
        };
    }

    private static string? Named(double w, double h)
    {
        (double a, double b) = (Math.Min(w, h), Math.Max(w, h));
        foreach (PageSize size in new[] { PageSize.A4, PageSize.A5, PageSize.Letter, PageSize.Legal })
        {
            if (Math.Abs(a - Math.Min(size.WidthMm, size.HeightMm)) < 1 && Math.Abs(b - Math.Max(size.WidthMm, size.HeightMm)) < 1)
            {
                return size.Name;
            }
        }

        return null;
    }

    private static IEnumerable<LabelObject> Convert(PubShape s, AssetStore assets)
    {
        var bounds = new RectMm(s.LeftPt * PtToMm, s.TopPt * PtToMm, Math.Max(0.1, s.WidthPt * PtToMm), Math.Max(0.1, s.HeightPt * PtToMm));
        double rotation = ((s.RotationDegrees % 360) + 360) % 360;
        string name = string.IsNullOrWhiteSpace(s.Name) ? s.Kind.ToString() : s.Name;

        switch (s.Kind)
        {
            case PubShapeKind.Picture when s.ImageBytes is { Length: > 0 } bytes:
                (int pw, int ph) = ImageDimensions.Probe(bytes);
                yield return new ImageObject
                {
                    Name = name,
                    Bounds = bounds,
                    RotationDegrees = rotation,
                    AssetId = assets.Add(bytes, s.ImageExtension),
                    SourcePixelWidth = pw,
                    SourcePixelHeight = ph,
                    FitMode = ImageFitMode.Stretch,
                };
                break;

            case PubShapeKind.Text:
                // A filled or outlined text box keeps its box as a shape underneath.
                if (s.Line is not null)
                {
                    yield return Shape(s, ShapeKind.Rectangle, bounds, rotation, name + " box", includeFill: true);
                }

                yield return Text(s, bounds, rotation, name, background: s.Line is null ? s.Fill : null);
                break;

            case PubShapeKind.Rectangle:
            case PubShapeKind.RoundRectangle:
            case PubShapeKind.Ellipse:
            case PubShapeKind.Line:
                ShapeKind kind = s.Kind switch
                {
                    PubShapeKind.RoundRectangle => ShapeKind.RoundRectangle,
                    PubShapeKind.Ellipse => ShapeKind.Ellipse,
                    PubShapeKind.Line => ShapeKind.Line,
                    _ => ShapeKind.Rectangle,
                };
                yield return Shape(s, kind, bounds, rotation, name, includeFill: true);
                if (s.Paragraphs.Count > 0)
                {
                    yield return Text(s, bounds, rotation, name + " text", background: null);
                }

                break;
        }
    }

    private static ShapeObject Shape(PubShape s, ShapeKind kind, RectMm bounds, double rotation, string name, bool includeFill) => new()
    {
        Name = name,
        Bounds = bounds,
        RotationDegrees = rotation,
        Shape = kind,
        Fill = includeFill && kind != ShapeKind.Line ? s.Fill ?? ColorRgba.Transparent : ColorRgba.Transparent,
        Stroke = s.Line ?? ColorRgba.Transparent,
        StrokeWidthMm = s.Line is null ? 0 : Math.Max(0.05, s.LineWeightPt * PtToMm),
        CornerRadiusMm = kind == ShapeKind.RoundRectangle ? Math.Min(bounds.Width, bounds.Height) * 0.1667 : 0,
    };

    private static TextObject Text(PubShape s, RectMm bounds, double rotation, string name, ColorRgba? background)
    {
        PubRun? first = s.Paragraphs.SelectMany(p => p.Runs).FirstOrDefault(r => r.Text.Length > 0);
        var text = new TextObject
        {
            Name = name,
            Bounds = bounds,
            RotationDegrees = rotation,
            FontFamily = first?.FontName is { Length: > 0 } font ? font : "Segoe UI",
            FontSizePt = first?.SizePt > 0 ? first.SizePt : 10,
            Bold = first?.Bold ?? false,
            Italic = first?.Italic ?? false,
            Underline = first?.Underline ?? false,
            Foreground = first?.Color ?? ColorRgba.Black,
            Background = background ?? ColorRgba.Transparent,
            HorizontalAlignment = s.Paragraphs.FirstOrDefault()?.Alignment ?? HAlign.Left,
            VerticalAlignment = s.VerticalAlignment,
            WordWrap = true,
            PaddingMm = new Thickness(s.InsetsPt.L * PtToMm, s.InsetsPt.T * PtToMm, s.InsetsPt.R * PtToMm, s.InsetsPt.B * PtToMm),
        };

        var content = new RichTextContent();
        foreach (PubParagraph p in s.Paragraphs)
        {
            var paragraph = new Paragraph { Alignment = p.Alignment == text.HorizontalAlignment ? null : p.Alignment };
            foreach (PubRun r in p.Runs.Where(r => r.Text.Length > 0))
            {
                // Only what differs from the text box's own style is stored on the run.
                paragraph.Runs.Add(new TextRun
                {
                    Text = r.Text,
                    FontFamily = r.FontName == text.FontFamily || r.FontName.Length == 0 ? null : r.FontName,
                    FontSizePt = r.SizePt <= 0 || Math.Abs(r.SizePt - text.FontSizePt) < 0.01 ? null : r.SizePt,
                    Bold = r.Bold == text.Bold ? null : r.Bold,
                    Italic = r.Italic == text.Italic ? null : r.Italic,
                    Underline = r.Underline == text.Underline ? null : r.Underline,
                    Foreground = r.Color == text.Foreground ? null : r.Color,
                });
            }

            content.Paragraphs.Add(paragraph);
        }

        if (content.Paragraphs.Count == 0)
        {
            content.Paragraphs.Add(new Paragraph());
        }

        text.Content = content;
        return text;
    }
}
