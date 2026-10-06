using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Serialization;
using LabelMaker.Core.Units;

namespace LabelMaker.Core.Designs;

/// <summary>
/// A ready-made label design from the built-in gallery (templates/designs/label-designs.json):
/// its sheet or roll and its objects, in millimetres from the label's top-left corner.
/// </summary>
public sealed class LabelDesign
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public DesignSize Size { get; set; } = new();
    public string Language { get; set; } = "en";
    public string? Background { get; set; }
    public List<DesignObject> Objects { get; set; } = new();

    /// <summary>What it is for, in a sentence (from label-design-notes.json).</summary>
    [JsonIgnore]
    public string Description { get; set; } = string.Empty;

    /// <summary>For one of the user's own designs (<see cref="MyDesigns"/>): the file it is kept in. Null for a built-in design.</summary>
    [JsonIgnore]
    public string? SavedPath { get; set; }

    /// <summary>For one of the user's own designs: the picture saved with it.</summary>
    [JsonIgnore]
    public byte[]? ThumbnailPng { get; set; }

    /// <summary>One of the user's own designs rather than a built-in one.</summary>
    [JsonIgnore]
    public bool IsMine => SavedPath is not null;

    /// <summary>The data fields its text and barcodes use ({Product}, {Price}…), in order.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> Fields =>
        Objects.SelectMany(o => DesignBuilder.FieldNames(o.Text ?? o.Data))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

public sealed class DesignSize
{
    /// <summary>"Formtec 3105", "Avery L7160" or "Thermal roll 50 × 30".</summary>
    public string Sheet { get; set; } = string.Empty;
    public double WidthMm { get; set; }
    public double HeightMm { get; set; }

    /// <summary>"sheet" or "thermal".</summary>
    public string Media { get; set; } = "sheet";

    /// <summary>"round" for round labels.</summary>
    public string? Shape { get; set; }

    public bool IsThermal => string.Equals(Media, "thermal", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One object of a design, as the designer wrote it; which fields matter depends on <see cref="Type"/>.</summary>
public sealed class DesignObject
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }

    // text
    public string? Text { get; set; }
    public string? Sample { get; set; }
    public string? Font { get; set; }
    public double SizePt { get; set; } = 10;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public string? Color { get; set; }
    public string? Align { get; set; }
    public string? Valign { get; set; }
    public double LetterSpacingPt { get; set; }
    public bool RightToLeft { get; set; }

    // barcode, qr
    public string? Symbology { get; set; }
    public string? Data { get; set; }
    public bool ShowDigits { get; set; } = true;

    // image
    public string? Placeholder { get; set; }
    public string? Crop { get; set; }
    public double CornerMm { get; set; }
    public double BorderMm { get; set; }
    public string? BorderColor { get; set; }

    // shape
    public string? Shape { get; set; }
    public string? Fill { get; set; }
    public string? Fill2 { get; set; }
    public double? GradientAngle { get; set; }
    public string? Stroke { get; set; }
    public double StrokeMm { get; set; }
    public string? Dash { get; set; }
    public string? Direction { get; set; }
    public int Points { get; set; } = 5;
    public double InnerRatio { get; set; }

    // table
    public int Rows { get; set; }
    public int Columns { get; set; }
    public List<string>? Cells { get; set; }
    public bool HeaderRow { get; set; }
    public string? HeaderFill { get; set; }
    public string? HeaderColor { get; set; }
}

/// <summary>The designs that come with LabelAxis.</summary>
public static class DesignCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static IReadOnlyList<LabelDesign>? _builtIn;

    /// <summary>Every built-in design, in the order of the gallery.</summary>
    public static IReadOnlyList<LabelDesign> BuiltIn => _builtIn ??= LoadBuiltIn();

    /// <summary>The groups in gallery order.</summary>
    public static IReadOnlyList<string> Groups(IEnumerable<LabelDesign> designs) =>
        designs.Select(d => d.Group).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Reads designs (a JSON array) and, optionally, their notes (a JSON object of id → sentence).</summary>
    public static IReadOnlyList<LabelDesign> Parse(string json, string? notesJson = null)
    {
        List<LabelDesign> designs = JsonSerializer.Deserialize<List<LabelDesign>>(json, Options) ?? new();
        if (notesJson is not null &&
            JsonSerializer.Deserialize<Dictionary<string, string>>(notesJson, Options) is { } notes)
        {
            foreach (LabelDesign d in designs)
            {
                d.Description = notes.GetValueOrDefault(d.Id, string.Empty);
            }
        }

        return designs;
    }

    private static IReadOnlyList<LabelDesign> LoadBuiltIn()
    {
        Assembly assembly = typeof(DesignCatalog).Assembly;
        string? Read(string suffix)
        {
            string? name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (name is null)
            {
                return null;
            }

            using Stream stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        return Read("label-designs.json") is { } json ? Parse(json, Read("label-design-notes.json")) : Array.Empty<LabelDesign>();
    }
}

/// <summary>How a design is turned into a project.</summary>
public sealed class DesignBuildOptions
{
    /// <summary>
    /// Keep the data fields ({Product}, {Price}…) in the text and barcodes, for printing from a
    /// spreadsheet; the design's sample words are kept as the fields' values until one is
    /// connected. Off: the sample words are written straight into the label, ready to type over.
    /// </summary>
    public bool UseDataFields { get; init; }

    /// <summary>Measures text, so a box is at least one line tall at the design's size.</summary>
    public ITextMeasurer? Measurer { get; init; }

    /// <summary>
    /// Makes the picture shown in a logo or photo box until the user puts their own in:
    /// (kind, box size) → PNG bytes and pixel size. Without one the box is left empty.
    /// </summary>
    public Func<string, SizeMm, (byte[] Png, int Width, int Height)?>? Placeholder { get; init; }
}

/// <summary>Turns a <see cref="LabelDesign"/> into a new project on the right sheet or roll.</summary>
public static partial class DesignBuilder
{
    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex FieldPattern();

    /// <summary>The field names in a text ("Batch {Batch}" → Batch).</summary>
    public static IEnumerable<string> FieldNames(string? text) =>
        text is null ? Enumerable.Empty<string>() : FieldPattern().Matches(text).Select(m => m.Groups[1].Value.Trim());

    /// <summary>The sheet or roll a design is made for: the built-in one of that name, or a roll of its size.</summary>
    public static LabelTemplate ResolveTemplate(LabelDesign design, TemplateStore store)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(store);
        DesignSize size = design.Size;
        if (!size.IsThermal)
        {
            string[] parts = size.Sheet.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2 &&
                store.All.Where(t => !t.IsRoll &&
                                     string.Equals(t.Brand, parts[0], StringComparison.OrdinalIgnoreCase) &&
                                     string.Equals(t.ProductCode, parts[1], StringComparison.OrdinalIgnoreCase))
                    .OrderBy(t => Math.Abs(t.Grid.LabelWidthMm - size.WidthMm) + Math.Abs(t.Grid.LabelHeightMm - size.HeightMm))
                    .FirstOrDefault() is { } sheet)
            {
                return sheet;
            }
        }

        // A thermal roll, or a sheet this PC's list does not have: a roll of the design's size.
        if (store.All.FirstOrDefault(t => t.IsRoll &&
                                          Math.Abs(t.Grid.LabelWidthMm - size.WidthMm) < 0.05 &&
                                          Math.Abs(t.Grid.LabelHeightMm - size.HeightMm) < 0.05 &&
                                          t.Grid.Columns == 1) is { } roll)
        {
            return roll;
        }

        string w = size.WidthMm.ToString("0.##", CultureInfo.InvariantCulture);
        string h = size.HeightMm.ToString("0.##", CultureInfo.InvariantCulture);
        return LabelTemplate.CreateRoll($"roll-{w}x{h}", $"{w} × {h} mm roll", size.WidthMm, size.HeightMm,
            cornerRadiusMm: string.Equals(size.Shape, "round", StringComparison.OrdinalIgnoreCase) ? Math.Min(size.WidthMm, size.HeightMm) / 2 : 0);
    }

    /// <summary>A new project with the design on its first page.</summary>
    public static ProjectDocument Build(LabelDesign design, TemplateStore store, DesignBuildOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(design);
        options ??= new DesignBuildOptions();

        LabelTemplate template = ResolveTemplate(design, store);
        ProjectModel project = ProjectModel.CreateNew(template, design.Name);
        var document = new ProjectDocument { Project = project };
        LabelDocument page = project.MasterLabel;
        page.Name = "Page 1";

        if (ColorRgba.TryParse(design.Background, out ColorRgba background) && background != ColorRgba.White && !background.IsTransparent)
        {
            page.Background.Enabled = true;
            page.Background.FillColor = background;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DesignObject o in design.Objects)
        {
            if (Make(o, options, document, values) is { } obj)
            {
                page.Objects.Add(obj);
            }
        }

        // The sample words stand in for the fields until a spreadsheet is connected.
        foreach ((string name, string value) in values)
        {
            project.Variables.Add(new CustomVariable { Name = name, Kind = VariableKind.Constant, Value = value, Description = "Sample from the design" });
        }

        return document;
    }

    private static LabelObject? Make(DesignObject o, DesignBuildOptions options, ProjectDocument document, Dictionary<string, string> values)
    {
        var bounds = new RectMm(o.X, o.Y, Math.Max(o.W, 0), Math.Max(o.H, 0));
        switch (o.Type.ToLowerInvariant())
        {
            case "text":
                return Text(o, bounds, options, values);
            case "barcode":
            case "qr":
                return Barcode(o, bounds, options, values);
            case "image":
                return Image(o, bounds, options, document);
            case "shape":
                return Shape(o, bounds);
            case "table":
                return Table(o, bounds);
            default:
                return null;
        }
    }

    // What a text or barcode says: the fields kept (their sample values noted), or the sample itself.
    private static string Content(string? text, string? sample, DesignBuildOptions options, Dictionary<string, string> values)
    {
        string written = text ?? sample ?? string.Empty;
        if (!FieldPattern().IsMatch(written))
        {
            return written;
        }

        if (!options.UseDataFields)
        {
            return sample ?? written;
        }

        if (sample is not null)
        {
            // "Produced {Date}" against "Produced 05/10/2026" gives Date = 05/10/2026.
            string pattern = "^" + string.Concat(FieldPattern().Split(written).Select((part, i) => i % 2 == 0 ? Regex.Escape(part) : "(.*?)")) + "$";
            Match match = Regex.Match(sample, pattern, RegexOptions.Singleline);
            if (match.Success)
            {
                string[] names = FieldNames(written).ToArray();
                for (int i = 0; i < names.Length; i++)
                {
                    values.TryAdd(names[i], match.Groups[i + 1].Value);
                }
            }
        }

        return written;
    }

    private static TextObject Text(DesignObject o, RectMm bounds, DesignBuildOptions options, Dictionary<string, string> values)
    {
        double size = o.SizePt > 0 ? o.SizePt : 10;
        var text = new TextObject
        {
            Name = o.Name,
            Content = RichTextContent.FromPlainText(Content(o.Text, o.Sample, options, values)),
            FontFamily = string.IsNullOrWhiteSpace(o.Font) ? "Segoe UI" : o.Font!,
            FontSizePt = size,
            Bold = o.Bold,
            Italic = o.Italic,
            Foreground = Colour(o.Color) ?? ColorRgba.Black,
            HorizontalAlignment = o.Align?.ToLowerInvariant() switch
            {
                "center" or "centre" => HorizontalAlignment.Center,
                "right" => HorizontalAlignment.Right,
                "justify" => HorizontalAlignment.Justify,
                _ => HorizontalAlignment.Left,
            },
            VerticalAlignment = o.Valign?.ToLowerInvariant() switch
            {
                "middle" or "center" => VerticalAlignment.Middle,
                "bottom" => VerticalAlignment.Bottom,
                _ => VerticalAlignment.Top,
            },
            Direction = o.RightToLeft ? TextDirection.RightToLeft : TextDirection.LeftToRight,
            LetterSpacingEm = o.LetterSpacingPt / size,
            PaddingMm = Thickness.Zero,
            // Longer words typed over the sample get smaller rather than spilling over the rest.
            AutoFit = TextAutoFit.ShrinkToFit,
            MinAutoFitPt = Math.Min(5, size),
        };

        // Each line of the sample must fit at the design's size: the designer drew the boxes to
        // the letters, the program also counts the font's space above and below them.
        if (options.Measurer is { } measurer)
        {
            int lines = Math.Max(1, text.Content.Paragraphs.Count);
            double needed = Math.Round(measurer.LineHeightMm(text.FontFamily, size) * lines, 2);
            if (needed > bounds.Height)
            {
                double extra = needed - bounds.Height;
                double top = text.VerticalAlignment switch
                {
                    VerticalAlignment.Middle => bounds.Y - (extra / 2),
                    VerticalAlignment.Bottom => bounds.Y - extra,
                    _ => bounds.Y,
                };
                bounds = bounds with { Y = top, Height = needed };
            }
        }

        text.Bounds = bounds;
        return text;
    }

    private static BarcodeObject Barcode(DesignObject o, RectMm bounds, DesignBuildOptions options, Dictionary<string, string> values)
    {
        BarcodeSymbology symbology = o.Type.Equals("qr", StringComparison.OrdinalIgnoreCase) ? BarcodeSymbology.QrCode : BarcodeSymbology.Code128;
        symbology = o.Symbology?.Replace(" ", "").Replace("-", "").ToUpperInvariant() switch
        {
            "EAN13" => BarcodeSymbology.Ean13,
            "EAN8" => BarcodeSymbology.Ean8,
            "UPCA" => BarcodeSymbology.UpcA,
            "CODE39" => BarcodeSymbology.Code39,
            "ITF14" => BarcodeSymbology.Itf14,
            "GS1128" => BarcodeSymbology.GS1_128,
            "CODE128" => BarcodeSymbology.Code128,
            "QR" or "QRCODE" => BarcodeSymbology.QrCode,
            "DATAMATRIX" => BarcodeSymbology.DataMatrix,
            "PDF417" => BarcodeSymbology.Pdf417,
            _ => symbology,
        };
        bool digits = o.ShowDigits && !symbology.Is2D();
        return new BarcodeObject
        {
            Name = o.Name,
            Bounds = bounds,
            AutoSize = false,
            Spec = new BarcodeSpec
            {
                Symbology = symbology,
                Data = Content(o.Data, o.Sample, options, values),
                HumanReadable = digits ? HumanReadablePosition.Below : HumanReadablePosition.None,
                ModuleWidthMm = 0.3,
                BarHeightMm = Math.Max(4, bounds.Height - (digits ? 3 : 0)),
                QuietZoneModules = symbology.Is2D() ? 0 : 10,
            },
        };
    }

    private static ImageObject Image(DesignObject o, RectMm bounds, DesignBuildOptions options, ProjectDocument document)
    {
        var image = new ImageObject
        {
            Name = o.Name,
            Bounds = bounds,
            FitMode = ImageFitMode.Fill,
            ClipShape = o.Crop?.ToLowerInvariant() switch
            {
                "round" or "circle" or "ellipse" => ImageClipShape.Ellipse,
                "rounded" => ImageClipShape.RoundedRectangle,
                _ => ImageClipShape.None,
            },
            ClipCornerRadiusMm = o.CornerMm > 0 ? o.CornerMm : 2,
            BorderWidthMm = o.BorderMm,
            BorderColor = Colour(o.BorderColor) ?? ColorRgba.Black,
        };

        if (options.Placeholder?.Invoke(o.Placeholder ?? "logo", new SizeMm(bounds.Width, bounds.Height)) is { } picture)
        {
            image.AssetId = document.Assets.Add(picture.Png, ".png");
            image.SourceFileName = $"{o.Placeholder ?? "logo"}.png";
            image.SourcePixelWidth = picture.Width;
            image.SourcePixelHeight = picture.Height;
        }

        return image;
    }

    private static ShapeObject Shape(DesignObject o, RectMm bounds)
    {
        ColorRgba? fill = Colour(o.Fill);
        ColorRgba? stroke = Colour(o.Stroke);
        var shape = new ShapeObject
        {
            Name = o.Name,
            Bounds = bounds,
            Fill = fill ?? ColorRgba.Transparent,
            Stroke = stroke ?? ColorRgba.Transparent,
            StrokeWidthMm = stroke is null ? 0 : o.StrokeMm,
            DashStyle = o.Dash?.ToLowerInvariant() switch
            {
                "dashed" or "dash" => DashStyle.Dash,
                "dotted" or "dot" => DashStyle.Dot,
                _ => DashStyle.Solid,
            },
            GradientTo = fill is null ? null : Colour(o.Fill2),
            GradientAngleDegrees = o.GradientAngle ?? 90,
            CornerRadiusMm = o.CornerMm > 0 ? o.CornerMm : 1,
        };

        switch (o.Shape?.ToLowerInvariant())
        {
            case "roundrectangle":
                shape.Shape = ShapeKind.RoundRectangle;
                break;
            case "ellipse":
            case "circle":
                shape.Shape = ShapeKind.Ellipse;
                break;
            case "line":
            {
                // A line is drawn across the middle of its box; an upright one is a level line turned a quarter round.
                shape.Shape = ShapeKind.Line;
                shape.Fill = ColorRgba.Transparent;
                if (bounds.Height <= 0.001 || bounds.Width > bounds.Height)
                {
                    shape.Bounds = new RectMm(bounds.X, bounds.Y - 0.5, bounds.Width, 1);
                }
                else
                {
                    double cx = bounds.X, cy = bounds.Y + (bounds.Height / 2);
                    shape.Bounds = new RectMm(cx - (bounds.Height / 2), cy - 0.5, bounds.Height, 1);
                    shape.RotationDegrees = 90;
                }

                break;
            }

            case "arrow":
                // A solid block arrow (head and shaft), drawn dot by dot so it fills like the design.
                shape.Shape = ShapeKind.Custom;
                shape.ClosedPath = true;
                shape.CustomPoints = Arrow(o.Direction);
                if (fill is null && stroke is { } line)
                {
                    shape.Fill = line;
                }

                break;
            case "star":
            case "polygon":
            {
                shape.Shape = ShapeKind.Polygon;
                shape.PolygonPoints = Math.Clamp(o.Points, 3, 64);
                shape.StarInnerRatio = o.Shape.Equals("star", StringComparison.OrdinalIgnoreCase)
                    ? (o.InnerRatio is > 0 and < 1 ? o.InnerRatio : 0.5)
                    : 0;
                // Drawn in the largest circle that fits the box, as designed.
                double side = Math.Min(bounds.Width, bounds.Height);
                shape.Bounds = new RectMm(bounds.Center.X - (side / 2), bounds.Center.Y - (side / 2), side, side);
                break;
            }

            default:
                shape.Shape = ShapeKind.Rectangle;
                break;
        }

        return shape;
    }

    private static List<PointMm> Arrow(string? direction)
    {
        (double U, double V)[] outline = { (0, .5), (.45, 1), (.45, .68), (1, .68), (1, .32), (.45, .32), (.45, 0) };
        return outline.Select(p => direction?.ToLowerInvariant() switch
        {
            "up" => new PointMm(p.V, p.U),
            "down" => new PointMm(p.V, 1 - p.U),
            "left" => new PointMm(p.U, p.V),
            _ => new PointMm(1 - p.U, p.V),
        }).ToList();
    }

    private static TableObject Table(DesignObject o, RectMm bounds)
    {
        var table = new TableObject
        {
            Name = o.Name,
            Bounds = bounds,
            Rows = Math.Max(1, o.Rows),
            Columns = Math.Max(1, o.Columns),
            Cells = o.Cells?.ToList() ?? new List<string>(),
            FontFamily = string.IsNullOrWhiteSpace(o.Font) ? "Segoe UI" : o.Font!,
            FontSizePt = o.SizePt > 0 ? o.SizePt : 8,
            CellAlignment = o.Align?.ToLowerInvariant() switch
            {
                "center" or "centre" => HorizontalAlignment.Center,
                "right" => HorizontalAlignment.Right,
                _ => HorizontalAlignment.Left,
            },
            BorderWidthMm = o.BorderMm,
            BorderColor = Colour(o.BorderColor) ?? ColorRgba.Black,
            FirstRowIsHeader = o.HeaderRow,
            HeaderFill = Colour(o.HeaderFill) ?? ColorRgba.Transparent,
            TextColor = Colour(o.Color) ?? ColorRgba.Black,
            HeaderTextColor = Colour(o.HeaderColor),
            Bold = o.Bold,
            CellPaddingMm = 0.8,
        };
        table.ColumnWeights = Enumerable.Repeat(1.0, table.Columns).ToList();
        table.RowWeights = Enumerable.Repeat(1.0, table.Rows).ToList();
        table.Normalize();
        return table;
    }

    private static ColorRgba? Colour(string? text) =>
        !string.IsNullOrWhiteSpace(text) && ColorRgba.TryParse(text, out ColorRgba c) ? c : null;
}
