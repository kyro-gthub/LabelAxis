namespace LabelMaker.Core.Verification;

/// <summary>A picture in shades of grey: 0 is black, 255 is white, one byte per pixel, row by row.</summary>
public sealed class GrayImage
{
    public GrayImage(byte[] pixels, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0 || height <= 0 || pixels.Length < width * height)
        {
            throw new ArgumentException("The picture's size does not match its pixels.", nameof(pixels));
        }

        Pixels = pixels;
        Width = width;
        Height = height;
    }

    public byte[] Pixels { get; }
    public int Width { get; }
    public int Height { get; }

    /// <summary>The reflectance (0 to 1) at a point between pixels, by bilinear interpolation.</summary>
    public double At(double x, double y)
    {
        x = Math.Clamp(x, 0, Width - 1.001);
        y = Math.Clamp(y, 0, Height - 1.001);
        int x0 = (int)x, y0 = (int)y;
        double fx = x - x0, fy = y - y0;
        int i = (y0 * Width) + x0;
        double top = (Pixels[i] * (1 - fx)) + (Pixels[i + 1] * fx);
        double bottom = (Pixels[i + Width] * (1 - fx)) + (Pixels[i + Width + 1] * fx);
        return ((top * (1 - fy)) + (bottom * fy)) / 255.0;
    }

    public bool Contains(double x, double y) => x >= 0 && y >= 0 && x <= Width - 1 && y <= Height - 1;

    /// <summary>A picture from 32-bit BGRA pixels (as WPF and cameras give them).</summary>
    public static GrayImage FromBgra(ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        var gray = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> row = bgra.Slice(y * stride, width * 4);
            for (int x = 0; x < width; x++)
            {
                int b = row[x * 4], g = row[(x * 4) + 1], r = row[(x * 4) + 2];
                gray[(y * width) + x] = (byte)(((r * 77) + (g * 150) + (b * 29)) >> 8);
            }
        }

        return new GrayImage(gray, width, height);
    }
}
