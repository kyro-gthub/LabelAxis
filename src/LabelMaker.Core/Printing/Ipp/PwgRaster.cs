using System.Buffers.Binary;
using System.Text;

namespace LabelMaker.Core.Printing.Ipp;

/// <summary>One page for <see cref="PwgRaster"/>: its pixels, top row first, with no padding.</summary>
/// <param name="Pixels">Grey (1 byte per pixel) or RGB (3 bytes per pixel).</param>
public sealed record RasterPage(byte[] Pixels, int Width, int Height, bool Color);

/// <summary>
/// Writes PWG Raster (PWG 5102.4), the picture format every "IPP Everywhere" printer accepts
/// without a driver. Each page is a 1796-byte header and run-length compressed rows; the
/// printer prints the pixels at the stated resolution, so a 300 dpi raster of an A4 sheet comes
/// out at exactly the size it was drawn.
/// </summary>
public static class PwgRaster
{
    public const int HeaderSize = 1796;

    private const int ColorSpaceSGray = 18;
    private const int ColorSpaceSrgb = 19;

    /// <summary>Writes the sync word and every page.</summary>
    public static void Write(Stream output, IReadOnlyList<RasterPage> pages, int dpi, string pageSizeName, int copies = 1)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(pages);
        output.Write("RaS2"u8);
        foreach (RasterPage page in pages)
        {
            WritePage(output, page, dpi, pageSizeName, pages.Count, copies);
        }
    }

    public static void WritePage(Stream output, RasterPage page, int dpi, string pageSizeName, int totalPages, int copies = 1)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(page);
        int bpp = page.Color ? 3 : 1;
        if (page.Pixels.Length < page.Width * page.Height * bpp)
        {
            throw new ArgumentException("The page has fewer pixels than its size says.", nameof(page));
        }

        output.Write(Header(page, dpi, pageSizeName, totalPages, copies));
        WriteRows(output, page.Pixels, page.Width, page.Height, bpp);
    }

    internal static byte[] Header(RasterPage page, int dpi, string pageSizeName, int totalPages, int copies)
    {
        int bpp = page.Color ? 3 : 1;
        var h = new byte[HeaderSize];
        Ascii(h, 0, "PwgRaster");
        Ascii(h, 1668, string.Empty);                       // RenderingIntent: printer's choice
        Ascii(h, 1732, pageSizeName);
        U32(h, 276, dpi);                                   // HWResolution, cross-feed and feed
        U32(h, 280, dpi);
        U32(h, 340, Math.Max(1, copies));                   // NumCopies (the job attribute also says so)
        U32(h, 352, (int)Math.Round(page.Width * 72.0 / dpi));   // PageSize in points
        U32(h, 356, (int)Math.Round(page.Height * 72.0 / dpi));
        U32(h, 372, page.Width);
        U32(h, 376, page.Height);
        U32(h, 384, 8);                                     // BitsPerColor
        U32(h, 388, 8 * bpp);                               // BitsPerPixel
        U32(h, 392, page.Width * bpp);                      // BytesPerLine
        U32(h, 396, 0);                                     // ColorOrder: chunky
        U32(h, 400, page.Color ? ColorSpaceSrgb : ColorSpaceSGray);
        U32(h, 420, bpp);                                   // NumColors
        U32(h, 452, totalPages);                            // TotalPageCount
        U32(h, 456, 1);                                     // CrossFeedTransform
        U32(h, 460, 1);                                     // FeedTransform
        U32(h, 472, page.Width);                            // ImageBoxRight
        U32(h, 476, page.Height);                           // ImageBoxBottom
        return h;
    }

    /// <summary>
    /// Rows are run-length coded: a row-repeat byte (identical rows that follow), then packets
    /// of a count byte and pixels. A count of 0–127 repeats the next pixel 1–128 times; 129–255
    /// is followed by 128–2 different pixels.
    /// </summary>
    internal static void WriteRows(Stream output, byte[] pixels, int width, int height, int bpp)
    {
        int stride = width * bpp;
        var buffer = new MemoryStream(stride + 64);
        int y = 0;
        while (y < height)
        {
            // How many of the following rows are the same as this one.
            int repeat = 0;
            ReadOnlySpan<byte> row = pixels.AsSpan(y * stride, stride);
            while (repeat < 255 && y + repeat + 1 < height && row.SequenceEqual(pixels.AsSpan((y + repeat + 1) * stride, stride)))
            {
                repeat++;
            }

            buffer.SetLength(0);
            buffer.WriteByte((byte)repeat);
            EncodeRow(buffer, row, width, bpp);
            output.Write(buffer.GetBuffer(), 0, (int)buffer.Length);
            y += repeat + 1;
        }
    }

    private static void EncodeRow(Stream s, ReadOnlySpan<byte> row, int width, int bpp)
    {
        int x = 0;
        while (x < width)
        {
            // A run of the same pixel?
            int run = 1;
            while (run < 128 && x + run < width && Same(row, x, x + run, bpp))
            {
                run++;
            }

            if (run > 1)
            {
                s.WriteByte((byte)(run - 1));
                s.Write(row.Slice(x * bpp, bpp));
                x += run;
                continue;
            }

            // Different pixels, up to the next pair of equal ones.
            int start = x;
            int count = 0;
            while (count < 128 && x < width && !(x + 1 < width && Same(row, x, x + 1, bpp)))
            {
                x++;
                count++;
            }

            if (count == 1)
            {
                s.WriteByte(0);                 // one pixel, "repeated" once
                s.Write(row.Slice(start * bpp, bpp));
            }
            else
            {
                s.WriteByte((byte)(257 - count));
                s.Write(row.Slice(start * bpp, count * bpp));
            }
        }
    }

    private static bool Same(ReadOnlySpan<byte> row, int a, int b, int bpp) =>
        row.Slice(a * bpp, bpp).SequenceEqual(row.Slice(b * bpp, bpp));

    private static void U32(byte[] h, int offset, int value) => BinaryPrimitives.WriteUInt32BigEndian(h.AsSpan(offset), (uint)value);

    private static void Ascii(byte[] h, int offset, string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        Array.Copy(bytes, 0, h, offset, Math.Min(bytes.Length, 63));
    }

    /// <summary>The PWG media name for a paper size, e.g. "iso_a4_210x297mm"; a custom name for anything else.</summary>
    public static string MediaName(double widthMm, double heightMm)
    {
        double w = Math.Min(widthMm, heightMm), hgt = Math.Max(widthMm, heightMm);
        static bool Near(double a, double b) => Math.Abs(a - b) < 1.0;
        return true switch
        {
            _ when Near(w, 210) && Near(hgt, 297) => "iso_a4_210x297mm",
            _ when Near(w, 148) && Near(hgt, 210) => "iso_a5_148x210mm",
            _ when Near(w, 105) && Near(hgt, 148) => "iso_a6_105x148mm",
            _ when Near(w, 297) && Near(hgt, 420) => "iso_a3_297x420mm",
            _ when Near(w, 176) && Near(hgt, 250) => "iso_b5_176x250mm",
            _ when Near(w, 215.9) && Near(hgt, 279.4) => "na_letter_8.5x11in",
            _ when Near(w, 215.9) && Near(hgt, 355.6) => "na_legal_8.5x14in",
            _ => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"custom_labelaxis_{w:0.##}x{hgt:0.##}mm"),
        };
    }
}
