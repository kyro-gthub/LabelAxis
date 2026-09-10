using LabelMaker.Core.Model.Objects;

namespace LabelMaker.Core.Model;

public enum BackgroundScope
{
    /// <summary>Painted inside each label's bounds.</summary>
    Label = 0,

    /// <summary>Painted once across the whole sheet page, behind every label.</summary>
    Page = 1,
}

/// <summary>
/// Optional background fill/image for a label or page. Locking prevents the
/// designer from selecting or moving it by accident (spec §4).
/// </summary>
public sealed class BackgroundSpec
{
    public bool Enabled { get; set; }

    public BackgroundScope Scope { get; set; } = BackgroundScope.Label;

    public ColorRgba FillColor { get; set; } = ColorRgba.Transparent;

    /// <summary>Asset hash of the background image, or null for a solid fill only.</summary>
    public string? AssetId { get; set; }

    public string? SourceFileName { get; set; }

    public ImageFitMode FitMode { get; set; } = ImageFitMode.Fill;

    public ImageAdjustments Adjustments { get; set; } = new();

    /// <summary>0..1 overall opacity of the background layer.</summary>
    public double Opacity { get; set; } = 1.0;

    public double RotationDegrees { get; set; }

    /// <summary>When true the background cannot be selected or moved on the canvas.</summary>
    public bool Locked { get; set; } = true;

    public int SourcePixelWidth { get; set; }
    public int SourcePixelHeight { get; set; }

    public bool HasImage => !string.IsNullOrEmpty(AssetId);

    public BackgroundSpec Clone() => new()
    {
        Enabled = Enabled,
        Scope = Scope,
        FillColor = FillColor,
        AssetId = AssetId,
        SourceFileName = SourceFileName,
        FitMode = FitMode,
        Adjustments = Adjustments.Clone(),
        Opacity = Opacity,
        RotationDegrees = RotationDegrees,
        Locked = Locked,
        SourcePixelWidth = SourcePixelWidth,
        SourcePixelHeight = SourcePixelHeight,
    };
}
