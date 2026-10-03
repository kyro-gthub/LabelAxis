namespace LabelMaker.Core.Printing.Ipp;

/// <summary>How a network printer is sent a job without a driver.</summary>
public enum IppDocumentFormat
{
    /// <summary>The printer accepts nothing this app can make.</summary>
    None = 0,

    /// <summary>PWG Raster: pictures of each sheet at the printer's own resolution.</summary>
    PwgRaster = 1,

    /// <summary>PDF (holding a picture of each sheet at its exact paper size).</summary>
    Pdf = 2,
}

/// <summary>What a printer said about itself in answer to Get-Printer-Attributes.</summary>
public sealed record IppPrinterInfo
{
    public required string Uri { get; init; }

    /// <summary>"printer-make-and-model", or the printer's own name when that is missing.</summary>
    public string Model { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Location { get; init; } = string.Empty;

    public IReadOnlyList<string> Formats { get; init; } = Array.Empty<string>();

    public bool ColorSupported { get; init; }

    /// <summary>Resolutions offered for PWG Raster, dots per inch.</summary>
    public IReadOnlyList<int> RasterResolutions { get; init; } = Array.Empty<int>();

    /// <summary>PWG Raster types such as "srgb_8" and "sgray_8".</summary>
    public IReadOnlyList<string> RasterTypes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The format to send: PWG Raster when the printer takes it (exact, and what IPP Everywhere
    /// requires), otherwise PDF.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IppDocumentFormat BestFormat =>
        Formats.Contains("image/pwg-raster", StringComparer.OrdinalIgnoreCase) && RasterTypes.Count > 0 ? IppDocumentFormat.PwgRaster
        : Formats.Contains("application/pdf", StringComparer.OrdinalIgnoreCase) ? IppDocumentFormat.Pdf
        : IppDocumentFormat.None;

    /// <summary>Colour raster when the printer prints colour and takes it, grey otherwise.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool RasterInColor =>
        (ColorSupported && RasterTypes.Contains("srgb_8", StringComparer.OrdinalIgnoreCase)) ||
        !RasterTypes.Contains("sgray_8", StringComparer.OrdinalIgnoreCase);

    /// <summary>300 dpi when offered (sharp and quick to send), else the nearest the printer has.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int RasterDpi => RasterResolutions.Count == 0 ? 300
        : RasterResolutions.Contains(300) ? 300
        : RasterResolutions.OrderBy(r => Math.Abs(r - 300)).First();

    public string Describe() => BestFormat switch
    {
        IppDocumentFormat.PwgRaster => $"prints directly as a {RasterDpi} dpi picture{(RasterInColor ? " in colour" : " in grey")}",
        IppDocumentFormat.Pdf => "prints directly as PDF",
        _ => "does not take a format LabelAxis can send; add it in Windows instead",
    };

    public static IppPrinterInfo From(string uri, IppMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        string model = response.Find("printer-make-and-model")?.Text ?? string.Empty;
        string name = response.Find("printer-name")?.Text ?? string.Empty;
        return new IppPrinterInfo
        {
            Uri = uri,
            Model = model.Length > 0 ? model : name,
            Name = name,
            Location = response.Find("printer-location")?.Text ?? string.Empty,
            Formats = response.Find("document-format-supported")?.Texts.ToList() ?? new List<string>(),
            ColorSupported = response.Find("color-supported")?.Boolean ?? false,
            RasterResolutions = response.Find("pwg-raster-document-resolution-supported")?.ResolutionsDpi.Distinct().ToList() ?? new List<int>(),
            RasterTypes = response.Find("pwg-raster-document-type-supported")?.Texts.ToList() ?? new List<string>(),
        };
    }
}
