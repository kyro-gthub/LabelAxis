using LabelMaker.Core.Barcodes;

namespace LabelMaker.Core.Verification;

/// <summary>A barcode quality grade, A (best) to F (fails), as ISO/IEC 15416 and 15415 grade them.</summary>
public enum QualityGrade
{
    F = 0,
    D = 1,
    C = 2,
    B = 3,
    A = 4,
}

/// <summary>One measured quality: its value as shown, its grade, and what it means.</summary>
public sealed record QualityParameter(string Name, string Value, QualityGrade? Grade, string Meaning);

/// <summary>The result of checking a barcode in a picture.</summary>
public sealed class BarcodeQualityReport
{
    public bool Found { get; init; }

    /// <summary>The barcode type as LabelAxis names it ("Code 128"), when found.</summary>
    public string Symbology { get; init; } = string.Empty;

    public BarcodeSymbology? Kind { get; init; }

    /// <summary>What the barcode says.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>The overall grade, and as a number from 0.0 to 4.0 (the ISO way to write it).</summary>
    public QualityGrade Grade { get; init; }

    public double Score { get; init; }

    public IReadOnlyList<QualityParameter> Parameters { get; init; } = Array.Empty<QualityParameter>();

    /// <summary>What to change to make it better, most important first.</summary>
    public IReadOnlyList<string> Advice { get; init; } = Array.Empty<string>();

    /// <summary>Where the barcode is in the picture (its corners or the scanned line), for drawing on it.</summary>
    public IReadOnlyList<(double X, double Y)> Outline { get; init; } = Array.Empty<(double, double)>();

    /// <summary>The size of one module (the narrowest bar) in the picture, in pixels.</summary>
    public double ModulePixels { get; init; }

    public static BarcodeQualityReport NotFound { get; } = new()
    {
        Found = false,
        Advice = new[]
        {
            "No barcode was found. Hold the label flat and fill most of the picture with the barcode, in even light without glare.",
        },
    };
}
