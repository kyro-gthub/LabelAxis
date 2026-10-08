using System.Collections.ObjectModel;
using System.Windows.Media;
using LabelMaker.App.Mvvm;
using LabelMaker.App.Services;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Units;

namespace LabelMaker.App.ViewModels;

/// <summary>The Properties panel for a picture: fit, crop, adjustments and border.</summary>
public sealed partial class SelectionProperties
{
    public IReadOnlyList<ImageFitMode> ImageFitModes { get; } = Enum.GetValues<ImageFitMode>();

    public ImageFitMode FitMode
    {
        get => (Obj as ImageObject)?.FitMode ?? ImageFitMode.Fit;
        set
        {
            if (Obj is ImageObject i)
            {
                Set(nameof(FitMode), () => i.FitMode, v => i.FitMode = v, value);
            }
        }
    }

    /// <summary>0..1 (LabelObject.Opacity is shared by every object type, surfaced here for images).</summary>
    public double ImageOpacity
    {
        get => Round(Obj?.Opacity ?? 1.0);
        set => Set(nameof(ImageOpacity), () => Obj!.Opacity, v => Obj!.Opacity = v, Math.Clamp(value, 0, 1));
    }

    public double Brightness
    {
        get => Round((Obj as ImageObject)?.Adjustments.Brightness ?? 0);
        set
        {
            if (Obj is ImageObject i)
            {
                Set(nameof(Brightness), () => i.Adjustments.Brightness, v => i.Adjustments.Brightness = v, Math.Clamp(value, -1, 1));
            }
        }
    }

    public double Contrast
    {
        get => Round((Obj as ImageObject)?.Adjustments.Contrast ?? 0);
        set
        {
            if (Obj is ImageObject i)
            {
                Set(nameof(Contrast), () => i.Adjustments.Contrast, v => i.Adjustments.Contrast = v, Math.Clamp(value, -1, 1));
            }
        }
    }

    public bool Grayscale
    {
        get => (Obj as ImageObject)?.Adjustments.Grayscale ?? false;
        set
        {
            if (Obj is ImageObject i)
            {
                Set(nameof(Grayscale), () => i.Adjustments.Grayscale, v => i.Adjustments.Grayscale = v, value);
            }
        }
    }

    public bool FlipHorizontal
    {
        get => (Obj as ImageObject)?.Adjustments.FlipHorizontal ?? false;
        set
        {
            if (Obj is ImageObject i)
            {
                Set(nameof(FlipHorizontal), () => i.Adjustments.FlipHorizontal, v => i.Adjustments.FlipHorizontal = v, value);
            }
        }
    }

    public bool FlipVertical
    {
        get => (Obj as ImageObject)?.Adjustments.FlipVertical ?? false;
        set
        {
            if (Obj is ImageObject i)
            {
                Set(nameof(FlipVertical), () => i.Adjustments.FlipVertical, v => i.Adjustments.FlipVertical = v, value);
            }
        }
    }

    public string? ImageSourceFileName => (Obj as ImageObject)?.SourceFileName;

    /// <summary>
    /// Stretch means "fill the box, ignore the aspect ratio". Exposed as a plain
    /// switch because that is the question people actually ask when resizing.
    /// </summary>
    public bool AllowStretch
    {
        get => (Obj as ImageObject)?.FitMode == ImageFitMode.Stretch;
        set
        {
            if (Obj is ImageObject i)
            {
                Set(nameof(AllowStretch), () => i.FitMode, v => i.FitMode = v,
                    value ? ImageFitMode.Stretch : ImageFitMode.Fit);
                OnPropertyChanged(nameof(FitMode));
            }
        }
    }

    public string CropSummary
    {
        get
        {
            if (Obj is not ImageObject i)
            {
                return "—";
            }

            ImageAdjustments a = i.Adjustments;
            return a.HasCrop
                ? $"{a.CropWidth * 100:0}% × {a.CropHeight * 100:0}% from ({a.CropX * 100:0}%, {a.CropY * 100:0}%)"
                : "Whole image";
        }
    }

    public bool HasCrop => (Obj as ImageObject)?.Adjustments.HasCrop == true;

    public static IReadOnlyList<ImageClipShape> ClipShapes { get; } = Enum.GetValues<ImageClipShape>();

    /// <summary>Crop-to-shape: clips the image to a circle or rounded box.</summary>
    public ImageClipShape ClipShape
    {
        get => (Obj as ImageObject)?.ClipShape ?? ImageClipShape.None;
        set
        {
            if (Obj is ImageObject i)
            {
                Set(nameof(ClipShape), () => i.ClipShape, v => i.ClipShape = v, value);
                OnPropertyChanged(nameof(IsRoundedClip));
            }
        }
    }

    public bool IsRoundedClip => (Obj as ImageObject)?.ClipShape == ImageClipShape.RoundedRectangle;

    public double ClipCornerRadiusMm
    {
        get => Out((Obj as ImageObject)?.ClipCornerRadiusMm ?? 3);
        set
        {
            if (Obj is ImageObject i)
            {
                Set(nameof(ClipCornerRadiusMm), () => i.ClipCornerRadiusMm, v => i.ClipCornerRadiusMm = Math.Max(0, v), In(value));
            }
        }
    }

    // ---- image outline border ----

    public static IReadOnlyList<ImageBorderLines> ImageBorderLineChoices { get; } = Enum.GetValues<ImageBorderLines>();

    /// <summary>Ticking it gives the image a thin black line; unticking removes the border.</summary>
    public bool ImageHasBorder
    {
        get => (Obj as ImageObject)?.BorderWidthMm > 0.001;
        set
        {
            if (Obj is ImageObject i && value != ImageHasBorder)
            {
                Set(nameof(ImageHasBorder), () => i.BorderWidthMm, v => i.BorderWidthMm = v, value ? 0.5 : 0);
            }
        }
    }

    public double ImageBorderWidth
    {
        get => Out((Obj as ImageObject)?.BorderWidthMm ?? 0.5);
        set
        {
            if (Obj is ImageObject i)
            {
                Set(nameof(ImageBorderWidth), () => i.BorderWidthMm, v => i.BorderWidthMm = Math.Clamp(v, 0, 20), In(value));
            }
        }
    }

    public string ImageBorderHex
    {
        get => ((Obj as ImageObject)?.BorderColor ?? ColorRgba.Black).ToHex();
        set
        {
            if (Obj is ImageObject i && ColorRgba.TryParse(value, out ColorRgba c))
            {
                Set(nameof(ImageBorderHex), () => i.BorderColor, v => i.BorderColor = v, c);
            }
        }
    }

    public Brush ImageBorderBrush => ToBrush((Obj as ImageObject)?.BorderColor ?? ColorRgba.Black);

    public LabelMaker.Core.Model.Objects.DashStyle ImageBorderDash
    {
        get => (Obj as ImageObject)?.BorderDash ?? LabelMaker.Core.Model.Objects.DashStyle.Solid;
        set
        {
            if (Obj is ImageObject i)
            {
                Set(nameof(ImageBorderDash), () => i.BorderDash, v => i.BorderDash = v, value);
            }
        }
    }

    public ImageBorderLines ImageBorderLines
    {
        get => (Obj as ImageObject)?.BorderLines ?? Core.Model.Objects.ImageBorderLines.Single;
        set
        {
            if (Obj is ImageObject i)
            {
                Set(nameof(ImageBorderLines), () => i.BorderLines, v => i.BorderLines = v, value);
            }
        }
    }

    /// <summary>Effective print resolution at the current placed size — under ~200 DPI looks soft.</summary>
    public string ImageResolution
    {
        get
        {
            if (Obj is not ImageObject i || i.SourcePixelWidth <= 0)
            {
                return "—";
            }

            double dpi = i.EffectiveDpiAt(i.Bounds.Width * Math.Clamp(i.Adjustments.CropWidth, 0.01, 1));
            return $"{i.SourcePixelWidth} × {i.SourcePixelHeight} px · {dpi:0} DPI at this size";
        }
    }

    public void ClearCrop()
    {
        if (Obj is ImageObject i)
        {
            ImageAdjustments next = i.Adjustments.Clone();
            next.CropX = 0;
            next.CropY = 0;
            next.CropWidth = 1;
            next.CropHeight = 1;
            Set("Reset crop", () => i.Adjustments, v => i.Adjustments = v, next);
        }
    }
}
