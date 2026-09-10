using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Bulk;
using LabelMaker.Core.Data;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Units;
using LabelMaker.Core.Variables;

namespace LabelMaker.Core.Preflight;

public enum PreflightSeverity
{
    Error = 0,
    Warning = 1,
}

public enum PreflightCategory
{
    ObjectOutsideLabel,
    BarcodeOutsidePrintableArea,
    TextOverflow,
    MissingImage,
    LowResolutionImage,
    MissingFont,
    InvalidBarcode,
    DuplicateBarcode,
    EmptyRequiredField,
    LabelOutsidePrintableArea,
}

public sealed record PreflightIssue(
    PreflightSeverity Severity,
    PreflightCategory Category,
    string Message)
{
    public string? ObjectId { get; init; }
    public int? RecordIndex { get; init; }
    public string? PageId { get; init; }
}

public sealed class PreflightReport
{
    public required IReadOnlyList<PreflightIssue> Issues { get; init; }

    public IEnumerable<PreflightIssue> Errors => Issues.Where(i => i.Severity == PreflightSeverity.Error);
    public IEnumerable<PreflightIssue> Warnings => Issues.Where(i => i.Severity == PreflightSeverity.Warning);

    public bool HasErrors => Errors.Any();
    public bool IsClean => Issues.Count == 0;
}

/// <summary>Host-supplied capabilities the checker needs but the core cannot know.</summary>
public sealed class PreflightEnvironment
{
    public Func<string, bool> AssetExists { get; init; } = _ => true;

    /// <summary>Returns (pixelWidth, pixelHeight) for an asset, or null if unknown.</summary>
    public Func<string, (int Width, int Height)?> ImagePixelSize { get; init; } = _ => null;

    public Func<string, bool> FontInstalled { get; init; } = _ => true;

    /// <summary>Minimum acceptable effective DPI for a placed image.</summary>
    public double MinImageDpi { get; init; } = 150.0;

    public DuplicatePolicy BarcodeDuplicatePolicy { get; init; } = DuplicatePolicy.Warn;

    /// <summary>How many data records to sample when checking a variable-data job (0 = all).</summary>
    public int RecordSampleLimit { get; init; } = 500;
}

/// <summary>
/// Optional pre-print validation (spec §26). Separates hard <see cref="PreflightSeverity.Error"/>s
/// from <see cref="PreflightSeverity.Warning"/>s and points every issue at an
/// object and/or record so the UI can jump straight to it.
/// </summary>
public sealed class PreflightChecker
{
    private readonly PreflightEnvironment _env;

    public PreflightChecker(PreflightEnvironment? environment = null) =>
        _env = environment ?? new PreflightEnvironment();

    public PreflightReport Check(ProjectModel project, DataSet? data = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var issues = new List<PreflightIssue>();

        LabelTemplate? template = project.Template;
        if (template is not null)
        {
            CheckTemplateFit(template, issues);
        }

        LabelDocument master = project.MasterLabel;
        RectMm labelRect = master.Bounds;

        // Static structural checks on the master design.
        CheckDocument(master, labelRect, pageId: null, issues);

        // Variable-data checks across a record sample.
        var barcodeValues = new List<string>();
        if (data is { RowCount: > 0 })
        {
            int limit = _env.RecordSampleLimit <= 0 ? data.RowCount : Math.Min(_env.RecordSampleLimit, data.RowCount);
            for (int r = 0; r < limit; r++)
            {
                var ctx = new ResolveContext
                {
                    Record = data.Rows[r],
                    Variables = project.Variables,
                    RecordNumber = r + 1,
                    RecordCount = data.RowCount,
                    TemplateName = template?.Name ?? string.Empty,
                };
                var resolver = new VariableResolver(ctx);
                CheckResolvedRecord(master, labelRect, resolver, r, issues, barcodeValues);
            }
        }
        else
        {
            var resolver = new VariableResolver(new ResolveContext { Variables = project.Variables });
            CheckResolvedRecord(master, labelRect, resolver, recordIndex: null, issues, barcodeValues);
        }

        if (_env.BarcodeDuplicatePolicy != DuplicatePolicy.Ignore && barcodeValues.Count > 1)
        {
            DuplicateReport dup = DuplicateDetector.Check(barcodeValues, _env.BarcodeDuplicatePolicy);
            foreach (DuplicateGroup g in dup.Groups)
            {
                issues.Add(new PreflightIssue(
                    dup.Policy == DuplicatePolicy.Prevent ? PreflightSeverity.Error : PreflightSeverity.Warning,
                    PreflightCategory.DuplicateBarcode,
                    $"Duplicate barcode detected: {g.Value} ({g.Count} labels)"));
            }
        }

        // Standalone pages.
        foreach (LabelPage page in project.Pages)
        {
            CheckDocument(page.Document, page.Document.Bounds, page.Id, issues);
        }

        return new PreflightReport { Issues = issues };
    }

    private void CheckTemplateFit(LabelTemplate template, List<PreflightIssue> issues)
    {
        RectMm printable = TemplateGeometry.PrintableArea(template);
        foreach (LabelSlot slot in TemplateGeometry.AllSlots(template))
        {
            if (!printable.Contains(slot.RectMm))
            {
                issues.Add(new PreflightIssue(
                    PreflightSeverity.Warning,
                    PreflightCategory.LabelOutsidePrintableArea,
                    $"Label {slot.PositionLabel} extends past the printer's assumed printable area."));
                break; // one is enough to flag
            }
        }
    }

    private void CheckDocument(LabelDocument doc, RectMm labelRect, string? pageId, List<PreflightIssue> issues)
    {
        foreach (LabelObject obj in doc.Objects)
        {
            if (!obj.Visible)
            {
                continue;
            }

            RectMm bounds = obj.RotatedBounds;
            if (!labelRect.Inflate(0.2, 0.2).Contains(bounds))
            {
                issues.Add(new PreflightIssue(
                    PreflightSeverity.Warning, PreflightCategory.ObjectOutsideLabel,
                    $"'{Describe(obj)}' extends past the label edge.")
                { ObjectId = obj.Id, PageId = pageId });
            }

            switch (obj)
            {
                case ImageObject image:
                    CheckImage(image, pageId, issues);
                    break;
                case TextObject text:
                    CheckFont(text, pageId, issues);
                    break;
            }
        }
    }

    private void CheckImage(ImageObject image, string? pageId, List<PreflightIssue> issues)
    {
        if (string.IsNullOrEmpty(image.AssetId) || !_env.AssetExists(image.AssetId))
        {
            issues.Add(new PreflightIssue(
                PreflightSeverity.Error, PreflightCategory.MissingImage,
                $"Image '{image.SourceFileName ?? image.Name}' is missing from the project.")
            { ObjectId = image.Id, PageId = pageId });
            return;
        }

        (int Width, int Height)? size = _env.ImagePixelSize(image.AssetId) ??
            (image.SourcePixelWidth > 0 ? (image.SourcePixelWidth, image.SourcePixelHeight) : null);
        if (size is { Width: > 0 } px && image.Bounds.Width > 0)
        {
            double dpi = px.Width / (image.Bounds.Width / UnitConverter.MmPerInch);
            if (dpi < _env.MinImageDpi)
            {
                issues.Add(new PreflightIssue(
                    PreflightSeverity.Warning, PreflightCategory.LowResolutionImage,
                    $"Image '{image.SourceFileName ?? image.Name}' is ~{dpi:0} DPI at its current size (min {_env.MinImageDpi:0}).")
                { ObjectId = image.Id, PageId = pageId });
            }
        }
    }

    private void CheckFont(TextObject text, string? pageId, List<PreflightIssue> issues)
    {
        if (!_env.FontInstalled(text.FontFamily))
        {
            issues.Add(new PreflightIssue(
                PreflightSeverity.Warning, PreflightCategory.MissingFont,
                $"Font '{text.FontFamily}' is not installed; a substitute will be used.")
            { ObjectId = text.Id, PageId = pageId });
        }
    }

    private void CheckResolvedRecord(
        LabelDocument doc, RectMm labelRect, VariableResolver resolver,
        int? recordIndex, List<PreflightIssue> issues, List<string> barcodeValues)
    {
        foreach (LabelObject obj in doc.Objects)
        {
            if (!obj.Visible)
            {
                continue;
            }

            switch (obj)
            {
                case BarcodeObject barcode:
                {
                    string value = resolver.Resolve(barcode.Spec.Data);
                    barcodeValues.Add(value);

                    BarcodeValidationResult v = BarcodeValidator.Validate(barcode.Spec, value);
                    foreach (BarcodeIssue bi in v.Issues.Where(i => i.Severity == BarcodeIssueSeverity.Error))
                    {
                        issues.Add(new PreflightIssue(
                            PreflightSeverity.Error, PreflightCategory.InvalidBarcode,
                            $"Barcode '{Describe(obj)}'{RecordSuffix(recordIndex)}: {bi.Message}")
                        { ObjectId = obj.Id, RecordIndex = recordIndex });
                    }

                    if (v.IsValid && BarcodeEngine.TryEncode(barcode.Spec, value, out BarcodeSymbol? symbol, out _) && symbol is not null)
                    {
                        SizeMm natural = BarcodeLayout.NaturalSize(symbol, barcode.Spec);
                        RectMm effective = barcode.AutoSize
                            ? new RectMm(obj.Bounds.X, obj.Bounds.Y, natural.Width, natural.Height)
                            : obj.Bounds;
                        if (!labelRect.Inflate(0.1, 0.1).Contains(effective))
                        {
                            issues.Add(new PreflightIssue(
                                PreflightSeverity.Error, PreflightCategory.BarcodeOutsidePrintableArea,
                                $"Barcode '{Describe(obj)}'{RecordSuffix(recordIndex)} does not fit inside the label.")
                            { ObjectId = obj.Id, RecordIndex = recordIndex });
                        }
                    }

                    break;
                }

                case TextObject text:
                {
                    foreach (string field in text.GetReferencedFields())
                    {
                        string resolved = resolver.ResolveToken(field);
                        if (resolved.Length == 0 && IsPlainRequiredField(text))
                        {
                            issues.Add(new PreflightIssue(
                                PreflightSeverity.Warning, PreflightCategory.EmptyRequiredField,
                                $"Field '{{{field}}}' is empty{RecordSuffix(recordIndex)} for '{Describe(obj)}'.")
                            { ObjectId = obj.Id, RecordIndex = recordIndex });
                        }
                    }

                    break;
                }
            }
        }
    }

    private static bool IsPlainRequiredField(TextObject text) => text.IsPureField;

    private static string RecordSuffix(int? recordIndex) =>
        recordIndex is { } i ? $" (record {i + 1})" : string.Empty;

    private static string Describe(LabelObject obj) =>
        !string.IsNullOrWhiteSpace(obj.Name)
            ? obj.Name
            : obj.GetSearchableStrings().FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? obj.Kind.ToString();
}
