namespace LabelMaker.Core.Model.Objects;

public enum ImageFitMode
{
    /// <summary>Fill the box exactly, ignoring aspect ratio.</summary>
    Stretch = 0,

    /// <summary>Largest size that fits inside the box, aspect preserved (letterboxed).</summary>
    Fit = 1,

    /// <summary>Smallest size that covers the box, aspect preserved (cropped).</summary>
    Fill = 2,

    /// <summary>Native size, centred.</summary>
    Center = 3,

    /// <summary>Repeat at native size from the top-left.</summary>
    Tile = 4,
}

/// <summary>Outline the image is clipped to ("crop to a shape").</summary>
public enum ImageClipShape
{
    None = 0,
    Ellipse = 1,
    RoundedRectangle = 2,
}

/// <summary>Non-destructive per-image adjustments applied at render time.</summary>
public sealed class ImageAdjustments
{
    /// <summary>-1..+1 additive brightness.</summary>
    public double Brightness { get; set; }

    /// <summary>-1..+1 contrast (0 = unchanged).</summary>
    public double Contrast { get; set; }

    public bool FlipHorizontal { get; set; }
    public bool FlipVertical { get; set; }
    public bool Grayscale { get; set; }

    /// <summary>Normalised crop rectangle (0..1) into the source image.</summary>
    public double CropX { get; set; }
    public double CropY { get; set; }
    public double CropWidth { get; set; } = 1.0;
    public double CropHeight { get; set; } = 1.0;

    public bool HasCrop => CropX > 0 || CropY > 0 || CropWidth < 1.0 || CropHeight < 1.0;

    public ImageAdjustments Clone() => (ImageAdjustments)MemberwiseClone();
}

/// <summary>
/// An image placed on a label, or used as a watermark. The pixels live in the
/// project asset store; <see cref="AssetId"/> is the content hash key.
/// </summary>
public sealed class ImageObject : LabelObject
{
    public override ObjectKind Kind => ObjectKind.Image;

    /// <summary>SHA-256 asset key into the project archive's <c>assets/</c> folder.</summary>
    public string AssetId { get; set; } = string.Empty;

    /// <summary>Original file name, kept for the UI and for "replace image".</summary>
    public string? SourceFileName { get; set; }

    public ImageFitMode FitMode { get; set; } = ImageFitMode.Fit;
    public ImageAdjustments Adjustments { get; set; } = new();

    /// <summary>Clip the image to a circle or rounded box — a "shape crop".</summary>
    public ImageClipShape ClipShape { get; set; } = ImageClipShape.None;

    /// <summary>Corner radius in millimetres for <see cref="ImageClipShape.RoundedRectangle"/>.</summary>
    public double ClipCornerRadiusMm { get; set; } = 3.0;

    /// <summary>Draw at reduced alpha as a watermark behind other objects.</summary>
    public bool IsWatermark { get; set; }

    /// <summary>Native pixel size, cached from decode for the DPI preflight check.</summary>
    public int SourcePixelWidth { get; set; }
    public int SourcePixelHeight { get; set; }

    /// <summary>Effective DPI at the current placement size (0 when unknown).</summary>
    public double EffectiveDpiAt(double widthMm) =>
        widthMm <= 0 || SourcePixelWidth <= 0
            ? 0
            : SourcePixelWidth / (widthMm / Units.UnitConverter.MmPerInch);

    public override IEnumerable<string> GetSearchableStrings()
    {
        if (!string.IsNullOrEmpty(SourceFileName))
        {
            yield return SourceFileName!;
        }
    }

    public override LabelObject Clone()
    {
        var copy = new ImageObject
        {
            AssetId = AssetId,
            SourceFileName = SourceFileName,
            FitMode = FitMode,
            Adjustments = Adjustments.Clone(),
            ClipShape = ClipShape,
            ClipCornerRadiusMm = ClipCornerRadiusMm,
            IsWatermark = IsWatermark,
            SourcePixelWidth = SourcePixelWidth,
            SourcePixelHeight = SourcePixelHeight,
        };
        CopyBaseTo(copy);
        return copy;
    }
}
