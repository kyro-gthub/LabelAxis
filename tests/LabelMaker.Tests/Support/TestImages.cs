using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace LabelMaker.Tests.Support;

/// <summary>Real pictures for tests that draw them: small, valid files any decoder opens.</summary>
public static class TestImages
{
    /// <summary>A PNG of <paramref name="width"/> × <paramref name="height"/> pixels, all one colour.</summary>
    public static byte[] Png(int width, int height, byte r = 200, byte g = 40, byte b = 40, byte a = 255)
    {
        // Each row: filter byte 0, then RGBA per pixel.
        var raw = new byte[height * (1 + (width * 4))];
        for (int y = 0; y < height; y++)
        {
            int row = y * (1 + (width * 4));
            for (int x = 0; x < width; x++)
            {
                int p = row + 1 + (x * 4);
                raw[p] = r;
                raw[p + 1] = g;
                raw[p + 2] = b;
                raw[p + 3] = a;
            }
        }

        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            z.Write(raw);
        }

        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
        ihdr[8] = 8; // bits per channel
        ihdr[9] = 6; // RGBA

        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        Chunk(png, "IHDR", ihdr);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static void Chunk(Stream png, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        png.Write(number);
        byte[] typeAndData = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        png.Write(typeAndData);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32(typeAndData));
        png.Write(number);
    }

    private static uint Crc32(byte[] bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return ~crc;
    }
}
