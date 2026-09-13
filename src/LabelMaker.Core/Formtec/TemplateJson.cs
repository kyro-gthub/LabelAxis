using System.Text.Json;
using System.Text.Json.Serialization;

namespace LabelMaker.Core.Formtec;

/// <summary>
/// On-disk shape of a template file. Kept separate from <see cref="LabelTemplate"/>
/// so the domain model can evolve without breaking files, and so parsing is a
/// plain data mapping with no polymorphism.
/// </summary>
public sealed class TemplateDto
{
    [JsonPropertyName("schema")] public string Schema { get; set; } = LabelTemplate.CurrentSchema;
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("brand")] public string Brand { get; set; } = "Formtec";
    [JsonPropertyName("productCode")] public string ProductCode { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("categories")] public List<string> Categories { get; set; } = new();
    [JsonPropertyName("page")] public PageDto Page { get; set; } = new();
    [JsonPropertyName("orientation")] public string Orientation { get; set; } = "Portrait";
    [JsonPropertyName("printableMarginMm")] public MarginDto PrintableMarginMm { get; set; } = new(4, 4, 4, 4);
    [JsonPropertyName("grid")] public GridDto Grid { get; set; } = new();
    [JsonPropertyName("fillOrder")] public string FillOrder { get; set; } = "RowMajor";
    [JsonPropertyName("notes")] public string? Notes { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("media")] public MediaDto? Media { get; set; }

    /// <summary>Only written for roll templates; sheet files stay as they were.</summary>
    public sealed class MediaDto
    {
        [JsonPropertyName("kind")] public string Kind { get; set; } = "Sheet";
        [JsonPropertyName("gapMm")] public double GapMm { get; set; } = 3.0;
        [JsonPropertyName("sensing")] public string Sensing { get; set; } = "Gap";
    }

    public sealed class PageDto
    {
        [JsonPropertyName("size")] public string Size { get; set; } = "A4";
        [JsonPropertyName("widthMm")] public double WidthMm { get; set; } = 210.0;
        [JsonPropertyName("heightMm")] public double HeightMm { get; set; } = 297.0;
    }

    public sealed class MarginDto
    {
        public MarginDto()
        {
        }

        public MarginDto(double top, double right, double bottom, double left)
        {
            Top = top;
            Right = right;
            Bottom = bottom;
            Left = left;
        }

        [JsonPropertyName("top")] public double Top { get; set; }
        [JsonPropertyName("right")] public double Right { get; set; }
        [JsonPropertyName("bottom")] public double Bottom { get; set; }
        [JsonPropertyName("left")] public double Left { get; set; }
    }

    public sealed class GridDto
    {
        [JsonPropertyName("columns")] public int Columns { get; set; } = 1;
        [JsonPropertyName("rows")] public int Rows { get; set; } = 1;
        [JsonPropertyName("labelWidthMm")] public double LabelWidthMm { get; set; }
        [JsonPropertyName("labelHeightMm")] public double LabelHeightMm { get; set; }
        [JsonPropertyName("marginTopMm")] public double MarginTopMm { get; set; }
        [JsonPropertyName("marginLeftMm")] public double MarginLeftMm { get; set; }
        [JsonPropertyName("hGapMm")] public double HGapMm { get; set; }
        [JsonPropertyName("vGapMm")] public double VGapMm { get; set; }
        [JsonPropertyName("cornerRadiusMm")] public double CornerRadiusMm { get; set; }
        [JsonPropertyName("labelShape")] public string LabelShape { get; set; } = "Rectangle";
    }
}

public static class TemplateJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static LabelTemplate FromJson(string json, bool isUserDefined = false)
    {
        TemplateDto dto = JsonSerializer.Deserialize<TemplateDto>(json, Options)
                          ?? throw new FormatException("Empty template document.");
        return FromDto(dto, isUserDefined);
    }

    public static LabelTemplate FromStream(Stream stream, bool isUserDefined = false)
    {
        TemplateDto dto = JsonSerializer.Deserialize<TemplateDto>(stream, Options)
                          ?? throw new FormatException("Empty template document.");
        return FromDto(dto, isUserDefined);
    }

    public static string ToJson(LabelTemplate template) =>
        JsonSerializer.Serialize(ToDto(template), Options);

    public static LabelTemplate FromDto(TemplateDto dto, bool isUserDefined = false)
    {
        var page = string.IsNullOrWhiteSpace(dto.Page.Size) || dto.Page.WidthMm > 0
            ? new PageSize(string.IsNullOrWhiteSpace(dto.Page.Size) ? "Custom" : dto.Page.Size, dto.Page.WidthMm, dto.Page.HeightMm)
            : PageSize.FromName(dto.Page.Size);

        if (page.WidthMm <= 0 || page.HeightMm <= 0)
        {
            page = PageSize.FromName(dto.Page.Size);
        }

        return new LabelTemplate
        {
            Schema = string.IsNullOrWhiteSpace(dto.Schema) ? LabelTemplate.CurrentSchema : dto.Schema,
            Id = dto.Id,
            Brand = string.IsNullOrWhiteSpace(dto.Brand) ? "Formtec" : dto.Brand,
            ProductCode = dto.ProductCode,
            Name = dto.Name,
            Categories = dto.Categories ?? new List<string>(),
            Page = page,
            Orientation = ParseEnum(dto.Orientation, PageOrientation.Portrait),
            PrintableMarginMm = new MarginsMm(
                dto.PrintableMarginMm.Top, dto.PrintableMarginMm.Right,
                dto.PrintableMarginMm.Bottom, dto.PrintableMarginMm.Left),
            Grid = new LabelGrid
            {
                Columns = dto.Grid.Columns,
                Rows = dto.Grid.Rows,
                LabelWidthMm = dto.Grid.LabelWidthMm,
                LabelHeightMm = dto.Grid.LabelHeightMm,
                MarginTopMm = dto.Grid.MarginTopMm,
                MarginLeftMm = dto.Grid.MarginLeftMm,
                HGapMm = dto.Grid.HGapMm,
                VGapMm = dto.Grid.VGapMm,
                CornerRadiusMm = dto.Grid.CornerRadiusMm,
                LabelShape = ParseEnum(dto.Grid.LabelShape, LabelShape.Rectangle),
            },
            FillOrder = ParseEnum(dto.FillOrder, SheetFillOrder.RowMajor),
            Notes = dto.Notes,
            Source = dto.Source,
            IsUserDefined = isUserDefined,
            Media = ParseEnum(dto.Media?.Kind, LabelMedia.Sheet),
            Roll = dto.Media is { } m && ParseEnum(m.Kind, LabelMedia.Sheet) == LabelMedia.Roll
                ? new RollMedia { GapMm = Math.Max(0, m.GapMm), Sensing = ParseEnum(m.Sensing, MediaSensing.Gap) }
                : null,
        };
    }

    public static TemplateDto ToDto(LabelTemplate t) => new()
    {
        Schema = t.Schema,
        Id = t.Id,
        Brand = t.Brand,
        ProductCode = t.ProductCode,
        Name = t.Name,
        Categories = t.Categories,
        Page = new TemplateDto.PageDto { Size = t.Page.Name, WidthMm = t.Page.WidthMm, HeightMm = t.Page.HeightMm },
        Orientation = t.Orientation.ToString(),
        PrintableMarginMm = new TemplateDto.MarginDto(
            t.PrintableMarginMm.Top, t.PrintableMarginMm.Right, t.PrintableMarginMm.Bottom, t.PrintableMarginMm.Left),
        Grid = new TemplateDto.GridDto
        {
            Columns = t.Grid.Columns,
            Rows = t.Grid.Rows,
            LabelWidthMm = t.Grid.LabelWidthMm,
            LabelHeightMm = t.Grid.LabelHeightMm,
            MarginTopMm = t.Grid.MarginTopMm,
            MarginLeftMm = t.Grid.MarginLeftMm,
            HGapMm = t.Grid.HGapMm,
            VGapMm = t.Grid.VGapMm,
            CornerRadiusMm = t.Grid.CornerRadiusMm,
            LabelShape = t.Grid.LabelShape.ToString(),
        },
        FillOrder = t.FillOrder.ToString(),
        Notes = t.Notes,
        Source = t.Source,
        Media = t.IsRoll
            ? new TemplateDto.MediaDto { Kind = "Roll", GapMm = t.Roll?.GapMm ?? 3.0, Sensing = (t.Roll?.Sensing ?? MediaSensing.Gap).ToString() }
            : null,
    };

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct =>
        Enum.TryParse(value, ignoreCase: true, out TEnum parsed) ? parsed : fallback;
}
