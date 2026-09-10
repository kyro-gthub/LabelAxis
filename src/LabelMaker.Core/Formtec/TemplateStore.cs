using System.Globalization;
using System.Reflection;
using System.Text;

namespace LabelMaker.Core.Formtec;

/// <summary>Query for <see cref="TemplateStore.Search"/>.</summary>
public sealed class TemplateQuery
{
    public string? Text { get; set; }
    public string? Brand { get; set; }
    public string? Category { get; set; }

    /// <summary>Match label size within <see cref="DimensionToleranceMm"/>.</summary>
    public double? LabelWidthMm { get; set; }
    public double? LabelHeightMm { get; set; }
    public double DimensionToleranceMm { get; set; } = 1.0;

    public bool? UserDefinedOnly { get; set; }
}

/// <summary>
/// The in-memory catalogue of label templates. Built-ins are embedded JSON;
/// user templates come from a folder. Adding a Formtec model is dropping a
/// <c>.json</c> file — no code change (spec §3, §33).
/// </summary>
public sealed class TemplateStore
{
    private readonly Dictionary<string, LabelTemplate> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TemplateLoadError> _errors = new();

    public IReadOnlyCollection<LabelTemplate> All => _byId.Values;

    public IReadOnlyList<TemplateLoadError> LoadErrors => _errors;

    public string? UserTemplateDirectory { get; private set; }

    public LabelTemplate? Get(string id) => _byId.GetValueOrDefault(id);

    public bool Contains(string id) => _byId.ContainsKey(id);

    /// <summary>Load the templates compiled into <c>LabelMaker.Core</c>.</summary>
    public static TemplateStore CreateWithBuiltIns()
    {
        var store = new TemplateStore();
        store.LoadEmbedded();
        return store;
    }

    public void LoadEmbedded()
    {
        Assembly asm = typeof(TemplateStore).Assembly;
        foreach (string resource in asm.GetManifestResourceNames())
        {
            if (!resource.Contains(".Formtec.BuiltIn.", StringComparison.Ordinal) ||
                !resource.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using Stream? s = asm.GetManifestResourceStream(resource);
            if (s is null)
            {
                continue;
            }

            try
            {
                LabelTemplate t = TemplateJson.FromStream(s, isUserDefined: false);
                AddOrReplace(t, validate: true);
            }
            catch (Exception ex)
            {
                _errors.Add(new TemplateLoadError(resource, ex.Message));
            }
        }
    }

    /// <summary>Merge every <c>*.json</c> in a user folder; invalid files are recorded, not fatal.</summary>
    public void LoadUserDirectory(string directory)
    {
        UserTemplateDirectory = directory;
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                LabelTemplate t = TemplateJson.FromJson(File.ReadAllText(file), isUserDefined: true);
                AddOrReplace(t, validate: true);
            }
            catch (Exception ex)
            {
                _errors.Add(new TemplateLoadError(file, ex.Message));
            }
        }
    }

    public void AddOrReplace(LabelTemplate template, bool validate = true)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (string.IsNullOrWhiteSpace(template.Id))
        {
            throw new ArgumentException("Template id is required.", nameof(template));
        }

        if (validate)
        {
            TemplateValidationResult v = TemplateValidator.Validate(template);
            if (v.HasErrors)
            {
                throw new InvalidOperationException(
                    "Invalid template: " + string.Join("; ", v.Issues.Select(i => i.Message)));
            }
        }

        _byId[template.Id] = template;
    }

    public bool Remove(string id) => _byId.Remove(id);

    /// <summary>Duplicate a template under a new id and mark it user-defined.</summary>
    public LabelTemplate Duplicate(string sourceId, string newId, string? newName = null)
    {
        LabelTemplate src = Get(sourceId) ?? throw new KeyNotFoundException($"No template '{sourceId}'.");
        if (Contains(newId))
        {
            throw new InvalidOperationException($"Template id '{newId}' already exists.");
        }

        LabelTemplate copy = src with
        {
            Id = newId,
            Name = newName ?? $"{src.Name} (copy)",
            IsUserDefined = true,
            Source = "duplicated from " + sourceId,
        };
        AddOrReplace(copy);
        return copy;
    }

    public IReadOnlyList<LabelTemplate> Search(TemplateQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        IEnumerable<LabelTemplate> q = _byId.Values;

        if (query.UserDefinedOnly is { } udo)
        {
            q = q.Where(t => t.IsUserDefined == udo);
        }

        if (!string.IsNullOrWhiteSpace(query.Brand))
        {
            q = q.Where(t => t.Brand.Equals(query.Brand, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            q = q.Where(t => t.Categories.Any(c => c.Equals(query.Category, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            string text = query.Text.Trim();
            q = q.Where(t =>
                Contains(t.Id, text) ||
                Contains(t.Name, text) ||
                Contains(t.ProductCode, text) ||
                Contains(t.Brand, text) ||
                t.Categories.Any(c => Contains(c, text)));
        }

        if (query.LabelWidthMm is { } w)
        {
            q = q.Where(t => Math.Abs(t.Grid.LabelWidthMm - w) <= query.DimensionToleranceMm);
        }

        if (query.LabelHeightMm is { } h)
        {
            q = q.Where(t => Math.Abs(t.Grid.LabelHeightMm - h) <= query.DimensionToleranceMm);
        }

        return q.OrderBy(t => t.Brand)
                .ThenBy(t => t.ProductCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        static bool Contains(string? haystack, string needle) =>
            haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> Categories() => _byId.Values
        .SelectMany(t => t.Categories)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public IReadOnlyList<string> Brands() => _byId.Values
        .Select(t => t.Brand)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(b => b, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public void SaveUserTemplate(LabelTemplate template, string? directory = null)
    {
        directory ??= UserTemplateDirectory
                      ?? throw new InvalidOperationException("No user template directory configured.");
        Directory.CreateDirectory(directory);
        LabelTemplate toSave = template with { IsUserDefined = true };
        AddOrReplace(toSave);
        string path = Path.Combine(directory, Sanitize(toSave.Id) + ".json");
        File.WriteAllText(path, TemplateJson.ToJson(toSave));
    }

    public void ExportJson(string id, string path) =>
        File.WriteAllText(path, TemplateJson.ToJson(Get(id) ?? throw new KeyNotFoundException(id)));

    public LabelTemplate ImportJson(string path, bool markUserDefined = true)
    {
        LabelTemplate t = TemplateJson.FromJson(File.ReadAllText(path), markUserDefined);
        AddOrReplace(t);
        return t;
    }

    // ---- Bulk CSV import/export: one template per row (spec §33) ----

    private static readonly string[] CsvHeader =
    {
        "id", "brand", "productCode", "name", "categories", "pageSize", "pageWidthMm", "pageHeightMm",
        "orientation", "columns", "rows", "labelWidthMm", "labelHeightMm", "marginTopMm", "marginLeftMm",
        "hGapMm", "vGapMm", "cornerRadiusMm", "labelShape", "fillOrder",
        "printTopMm", "printRightMm", "printBottomMm", "printLeftMm",
    };

    public string ExportCsv(IEnumerable<LabelTemplate>? templates = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', CsvHeader));
        foreach (LabelTemplate t in (templates ?? _byId.Values).OrderBy(t => t.Id))
        {
            string[] row =
            {
                t.Id, t.Brand, t.ProductCode, t.Name, string.Join('|', t.Categories),
                t.Page.Name, F(t.Page.WidthMm), F(t.Page.HeightMm), t.Orientation.ToString(),
                t.Grid.Columns.ToString(CultureInfo.InvariantCulture), t.Grid.Rows.ToString(CultureInfo.InvariantCulture),
                F(t.Grid.LabelWidthMm), F(t.Grid.LabelHeightMm), F(t.Grid.MarginTopMm), F(t.Grid.MarginLeftMm),
                F(t.Grid.HGapMm), F(t.Grid.VGapMm), F(t.Grid.CornerRadiusMm), t.Grid.LabelShape.ToString(),
                t.FillOrder.ToString(),
                F(t.PrintableMarginMm.Top), F(t.PrintableMarginMm.Right), F(t.PrintableMarginMm.Bottom), F(t.PrintableMarginMm.Left),
            };
            sb.AppendLine(string.Join(',', row.Select(CsvEscape)));
        }

        return sb.ToString();

        static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
    }

    public IReadOnlyList<LabelTemplate> ImportCsv(string csv, bool markUserDefined = true)
    {
        var added = new List<LabelTemplate>();
        string[] lines = csv.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
        {
            return added;
        }

        string[] header = SplitCsv(lines[0]).Select(h => h.Trim()).ToArray();
        int Idx(string name) => Array.FindIndex(header, h => h.Equals(name, StringComparison.OrdinalIgnoreCase));

        for (int i = 1; i < lines.Length; i++)
        {
            string[] cols = SplitCsv(lines[i]);
            string Cell(string name)
            {
                int idx = Idx(name);
                return idx >= 0 && idx < cols.Length ? cols[idx].Trim() : string.Empty;
            }

            double D(string name) => double.TryParse(Cell(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
            int I(string name) => int.TryParse(Cell(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

            string pageName = Cell("pageSize");
            double pw = D("pageWidthMm");
            double ph = D("pageHeightMm");
            PageSize page = pw > 0 && ph > 0 ? new PageSize(string.IsNullOrEmpty(pageName) ? "Custom" : pageName, pw, ph) : PageSize.FromName(pageName);

            var t = new LabelTemplate
            {
                Id = Cell("id"),
                Brand = Cell("brand") is { Length: > 0 } b ? b : "Formtec",
                ProductCode = Cell("productCode"),
                Name = Cell("name"),
                Categories = Cell("categories").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                Page = page,
                Orientation = Enum.TryParse(Cell("orientation"), true, out PageOrientation o) ? o : PageOrientation.Portrait,
                PrintableMarginMm = new MarginsMm(D("printTopMm"), D("printRightMm"), D("printBottomMm"), D("printLeftMm")),
                Grid = new LabelGrid
                {
                    Columns = I("columns"),
                    Rows = I("rows"),
                    LabelWidthMm = D("labelWidthMm"),
                    LabelHeightMm = D("labelHeightMm"),
                    MarginTopMm = D("marginTopMm"),
                    MarginLeftMm = D("marginLeftMm"),
                    HGapMm = D("hGapMm"),
                    VGapMm = D("vGapMm"),
                    CornerRadiusMm = D("cornerRadiusMm"),
                    LabelShape = Enum.TryParse(Cell("labelShape"), true, out LabelShape ls) ? ls : LabelShape.Rectangle,
                },
                FillOrder = Enum.TryParse(Cell("fillOrder"), true, out SheetFillOrder fo) ? fo : SheetFillOrder.RowMajor,
                IsUserDefined = markUserDefined,
                Source = "csv import",
            };

            try
            {
                AddOrReplace(t);
                added.Add(t);
            }
            catch (Exception ex)
            {
                _errors.Add(new TemplateLoadError($"csv row {i + 1}", ex.Message));
            }
        }

        return added;
    }

    private static string CsvEscape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? '"' + value.Replace("\"", "\"\"") + '"'
            : value;

    private static string[] SplitCsv(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    sb.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        result.Add(sb.ToString());
        return result.ToArray();
    }

    private static string Sanitize(string id)
    {
        Span<char> buffer = stackalloc char[id.Length];
        int n = 0;
        foreach (char c in id)
        {
            buffer[n++] = Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c;
        }

        return new string(buffer[..n]);
    }
}

public readonly record struct TemplateLoadError(string Source, string Message);
