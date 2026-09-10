using System.Buffers.Binary;

namespace LabelMaker.Core.Rendering;

/// <summary>
/// Reads pixel dimensions straight from image file headers — no decoder, no
/// System.Drawing. Supports PNG, JPEG, GIF, BMP. Returns (0, 0) when the format
/// is unknown or the header is truncated.
/// </summary>
public static class ImageDimensions
{
    public static (int Width, int Height) Probe(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 24)
        {
            return (0, 0);
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A, IHDR width/height as big-endian int at offset 16.
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            int w = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4));
            int h = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4));
            return (w, h);
        }

        // GIF: "GIF87a"/"GIF89a", logical screen width/height little-endian at offset 6.
        if (bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F')
        {
            int w = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(6, 2));
            int h = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(8, 2));
            return (w, h);
        }

        // BMP: "BM", DIB header width/height little-endian at offset 18/22.
        if (bytes[0] == 'B' && bytes[1] == 'M')
        {
            int w = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(18, 4));
            int h = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(22, 4)));
            return (w, h);
        }

        // JPEG: scan SOF0..SOF15 markers.
        if (bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            return ProbeJpeg(bytes);
        }

        return (0, 0);
    }

    private static (int Width, int Height) ProbeJpeg(ReadOnlySpan<byte> bytes)
    {
        int offset = 2;
        while (offset + 9 < bytes.Length)
        {
            if (bytes[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            byte marker = bytes[offset + 1];
            if (marker is 0xD8 or 0xD9 or (>= 0xD0 and <= 0xD7))
            {
                offset += 2;
                continue;
            }

            int segmentLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 2, 2));
            bool isSof = marker is (>= 0xC0 and <= 0xC3) or (>= 0xC5 and <= 0xC7)
                or (>= 0xC9 and <= 0xCB) or (>= 0xCD and <= 0xCF);
            if (isSof && offset + 9 < bytes.Length)
            {
                int h = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 5, 2));
                int w = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 7, 2));
                return (w, h);
            }

            offset += 2 + segmentLength;
        }

        return (0, 0);
    }
}
