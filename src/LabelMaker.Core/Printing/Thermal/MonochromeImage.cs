namespace LabelMaker.Core.Printing.Thermal;

/// <summary>
/// A 1-bit picture of one printed label: one bit per print-head dot, rows padded to whole
/// bytes, most significant bit first, 1 = a printed (black) dot.
/// </summary>
public sealed class MonochromeImage
{
    public MonochromeImage(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        BytesPerRow = (width + 7) / 8;
        Bits = new byte[BytesPerRow * height];
    }

    public int Width { get; }

    public int Height { get; }

    public int BytesPerRow { get; }

    public byte[] Bits { get; }

    public bool this[int x, int y]
    {
        get => (Bits[(y * BytesPerRow) + (x >> 3)] & (0x80 >> (x & 7))) != 0;
        set
        {
            int i = (y * BytesPerRow) + (x >> 3);
            byte mask = (byte)(0x80 >> (x & 7));
            Bits[i] = value ? (byte)(Bits[i] | mask) : (byte)(Bits[i] & ~mask);
        }
    }

    public ReadOnlySpan<byte> Row(int y) => Bits.AsSpan(y * BytesPerRow, BytesPerRow);

    /// <summary>Share of dots printed, 0–1 (a quick sanity check that something will print).</summary>
    public double Coverage
    {
        get
        {
            long black = 0;
            foreach (byte b in Bits)
            {
                black += System.Numerics.BitOperations.PopCount(b);
            }

            return (double)black / ((long)Width * Height);
        }
    }

    /// <summary>
    /// Converts 8-bit grey pixels (0 = black, 255 = white, <paramref name="width"/> per row) to dots.
    /// </summary>
    public static MonochromeImage FromGray(ReadOnlySpan<byte> gray, int width, int height, DitherMode mode, int threshold = 150)
    {
        if (gray.Length < (long)width * height)
        {
            throw new ArgumentException("Not enough pixels for the size given.", nameof(gray));
        }

        var image = new MonochromeImage(width, height);
        threshold = Math.Clamp(threshold, 1, 254);

        if (mode == DitherMode.Threshold)
        {
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    if (gray[row + x] < threshold)
                    {
                        image[x, y] = true;
                    }
                }
            }

            return image;
        }

        // Floyd–Steinberg error diffusion, two rows of error kept at a time.
        var current = new int[width + 2];
        var next = new int[width + 2];
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                int value = gray[row + x] + (current[x + 1] / 16);
                bool black = value < 128;
                image[x, y] = black;
                int error = value - (black ? 0 : 255);
                current[x + 2] += error * 7;
                next[x] += error * 3;
                next[x + 1] += error * 5;
                next[x + 2] += error;
            }

            (current, next) = (next, current);
            Array.Clear(next);
        }

        return image;
    }

    /// <summary>Blanks the dots inside the rectangle (clipped to the picture).</summary>
    public void Clear(int x, int y, int width, int height)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
        int x1 = Math.Min(Width, x + width), y1 = Math.Min(Height, y + height);
        for (int row = y0; row < y1; row++)
        {
            for (int col = x0; col < x1; col++)
            {
                this[col, row] = false;
            }
        }
    }

    /// <summary>A copy of the rectangle (clipped to the picture); null when nothing of it is inside.</summary>
    public MonochromeImage? Crop(int x, int y, int width, int height)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
        int x1 = Math.Min(Width, x + width), y1 = Math.Min(Height, y + height);
        if (x1 <= x0 || y1 <= y0)
        {
            return null;
        }

        var part = new MonochromeImage(x1 - x0, y1 - y0);
        for (int row = y0; row < y1; row++)
        {
            for (int col = x0; col < x1; col++)
            {
                if (this[col, row])
                {
                    part[col - x0, row - y0] = true;
                }
            }
        }

        return part;
    }

    /// <summary>Prints <paramref name="other"/>'s dots onto this picture with its top-left at (x, y).</summary>
    public void Overlay(MonochromeImage other, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(other);
        for (int row = Math.Max(0, -y); row < other.Height && y + row < Height; row++)
        {
            for (int col = Math.Max(0, -x); col < other.Width && x + col < Width; col++)
            {
                if (other[col, row])
                {
                    this[x + col, y + row] = true;
                }
            }
        }
    }

    /// <summary>The same picture turned half a turn, for printers that feed the roll the other way.</summary>
    public MonochromeImage Rotated180()
    {
        var turned = new MonochromeImage(Width, Height);
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (this[x, y])
                {
                    turned[Width - 1 - x, Height - 1 - y] = true;
                }
            }
        }

        return turned;
    }
}
