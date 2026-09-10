using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Units;

namespace LabelMaker.Core.Rendering;

/// <summary>Computes the destination rectangle for an image inside a target box, per fit mode.</summary>
public static class ImageFit
{
    public static RectMm Compute(ImageFitMode mode, RectMm target, double sourcePixelW, double sourcePixelH)
    {
        if (sourcePixelW <= 0 || sourcePixelH <= 0)
        {
            return target;
        }

        double srcAspect = sourcePixelW / sourcePixelH;
        double dstAspect = target.Width / target.Height;

        switch (mode)
        {
            case ImageFitMode.Stretch:
                return target;

            case ImageFitMode.Center:
            {
                double w = Math.Min(target.Width, sourcePixelW * MmPerPixelGuess(target, sourcePixelW, sourcePixelH));
                double h = Math.Min(target.Height, sourcePixelH * MmPerPixelGuess(target, sourcePixelW, sourcePixelH));
                return RectMm.FromCenter(target.Center, new SizeMm(w, h));
            }

            case ImageFitMode.Fit:
            {
                double scale = srcAspect > dstAspect ? target.Width / sourcePixelW : target.Height / sourcePixelH;
                var size = new SizeMm(sourcePixelW * scale, sourcePixelH * scale);
                return RectMm.FromCenter(target.Center, size);
            }

            case ImageFitMode.Fill:
            {
                double scale = srcAspect > dstAspect ? target.Height / sourcePixelH : target.Width / sourcePixelW;
                var size = new SizeMm(sourcePixelW * scale, sourcePixelH * scale);
                return RectMm.FromCenter(target.Center, size);
            }

            case ImageFitMode.Tile:
            default:
                return target;
        }
    }

    private static double MmPerPixelGuess(RectMm target, double w, double h) =>
        Math.Min(target.Width / w, target.Height / h);
}
