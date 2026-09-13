using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OpenMcdf;

namespace LabelMaker.Data.Publisher;

/// <summary>One text box (story) from a Publisher file, split into paragraphs.</summary>
public sealed record PublisherStory(int Number, IReadOnlyList<string> Paragraphs)
{
    public string Text => string.Join('\n', Paragraphs);
}

/// <summary>A picture found inside a Publisher file.</summary>
public sealed record PublisherImage(int Number, string Extension, byte[] Bytes, int Width, int Height);

/// <summary>What could be read out of a .pub file.</summary>
public sealed class PublisherContent
{
    public required IReadOnlyList<PublisherStory> Stories { get; init; }

    public required IReadOnlyList<PublisherImage> Images { get; init; }

    /// <summary>
    /// True when the text index was not understood and the text was recovered by
    /// scanning for readable characters instead: paragraphs may be split or ordered
    /// differently from the document.
    /// </summary>
    public bool TextIsApproximate { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Reads the text and pictures out of a Microsoft Publisher (.pub) file without
/// Publisher installed. A .pub file is an OLE compound file: the text of every text
/// box lives in the Quill "CONTENTS" stream (a chunk index pointing at a UTF-16 TEXT
/// chunk, with story lengths in STRS), and pictures are stored as PNG, JPEG or
/// bitmap blobs in the Escher streams.
/// <para>
/// Content only: page layout, positions, fonts, colours and shapes are not imported.
/// When the text index is missing or unfamiliar (older or unusual files), readable
/// text is recovered by scanning instead, and <see cref="PublisherContent.TextIsApproximate"/> says so.
/// </para>
/// </summary>
public static class PublisherReader
{
    private static readonly byte[] CompoundSignature = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private const int MinImageSide = 4;
    private const int MaxStreamBytes = 512 * 1024 * 1024;

    /// <summary>True when the file is a compound document that looks like Publisher's.</summary>
    public static bool LooksLikePublisher(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            Span<byte> head = stackalloc byte[8];
            if (stream.Read(head) != 8 || !head.SequenceEqual(CompoundSignature))
            {
                return false;
            }

            stream.Position = 0;
            using RootStorage root = RootStorage.Open(stream, StorageModeFlags.LeaveOpen);
            return root.ContainsEntry("Quill") || root.ContainsEntry("Contents") || root.ContainsEntry("Escher");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OpenMcdf.FileFormatException or InvalidDataException)
        {
            return false;
        }
    }

    public static PublisherContent Read(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Read(stream);
    }

    public static PublisherContent Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        RootStorage root;
        try
        {
            root = RootStorage.Open(stream, StorageModeFlags.LeaveOpen);
        }
        catch (Exception ex) when (ex is OpenMcdf.FileFormatException or InvalidDataException or IOException)
        {
            throw new DataSourceException("This file is not a Publisher document (it is not an OLE compound file).", ex);
        }

        using (root)
        {
            var notes = new List<string>();
            Dictionary<string, byte[]> streams = ReadAllStreams(root, string.Empty);
            if (streams.Count == 0)
            {
                throw new DataSourceException("The file contains no data streams.");
            }

            IReadOnlyList<PublisherStory> stories;
            bool approximate = false;

            byte[]? quill = streams.FirstOrDefault(s => s.Key.Equals("Quill/QuillSub/CONTENTS", StringComparison.OrdinalIgnoreCase)).Value;
            if (quill is not null && TryReadQuillText(quill, out stories))
            {
                // parsed from the text index
            }
            else
            {
                approximate = true;
                notes.Add(quill is null
                    ? "This file has no Publisher 2000-or-later text index; text was recovered by scanning and may be out of order."
                    : "The text index was not recognised; text was recovered by scanning and may be out of order.");
                byte[] source = quill ?? streams.FirstOrDefault(s => s.Key.Equals("Contents", StringComparison.OrdinalIgnoreCase)).Value
                                ?? streams.Values.OrderByDescending(b => b.Length).First();
                stories = ScanText(source);
            }

            List<PublisherImage> images = ExtractImages(streams);
            if (streams.Keys.Any(k => k.StartsWith("Escher", StringComparison.OrdinalIgnoreCase)) && images.Count == 0)
            {
                notes.Add("No PNG, JPEG or bitmap pictures were found. Drawings stored as Windows metafiles are not imported.");
            }

            return new PublisherContent
            {
                Stories = stories,
                Images = images,
                TextIsApproximate = approximate,
                Notes = notes,
            };
        }
    }

    // ---- streams ------------------------------------------------------------------

    private static Dictionary<string, byte[]> ReadAllStreams(Storage storage, string prefix)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (EntryInfo entry in storage.EnumerateEntries())
        {
            string path = prefix.Length == 0 ? entry.Name : prefix + "/" + entry.Name;
            if (entry.Type == EntryType.Storage)
            {
                foreach (KeyValuePair<string, byte[]> child in ReadAllStreams(storage.OpenStorage(entry.Name), path))
                {
                    result[child.Key] = child.Value;
                }
            }
            else if (entry.Length is > 0 and <= MaxStreamBytes)
            {
                using CfbStream s = storage.OpenStream(entry.Name);
                var bytes = new byte[entry.Length];
                s.ReadExactly(bytes);
                result[path] = bytes;
            }
        }

        return result;
    }

    // ---- text ---------------------------------------------------------------------

    /// <summary>A chunk in the Quill CONTENTS index: a four-letter type, where it starts and how long it is.</summary>
    internal readonly record struct QuillChunk(string Name, int Id, string Name2, int Offset, int Length);

    /// <summary>
    /// Reads the chunk index. It is a linked list of blocks: each starts with a count
    /// (at +2) and the offset of the next block (at +4, 0xFFFFFFFF ends the list),
    /// followed by 26-byte entries (4 unknown, 4 name, 2 id, 4 unknown, 4 name, 4 offset, 4 length).
    /// </summary>
    internal static List<QuillChunk> ReadChunkIndex(byte[] contents)
    {
        var chunks = new List<QuillChunk>();
        var visited = new HashSet<uint>();
        uint block = 0x18;

        while (block != 0xFFFFFFFF && block + 8 <= contents.Length && visited.Add(block))
        {
            int count = BinaryPrimitives.ReadUInt16LittleEndian(contents.AsSpan((int)block + 2));
            uint next = BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan((int)block + 4));
            int at = (int)block + 8;

            for (int i = 0; i < count && at + 26 <= contents.Length; i++, at += 26)
            {
                string name = Encoding.ASCII.GetString(contents, at + 4, 4);
                int id = BinaryPrimitives.ReadUInt16LittleEndian(contents.AsSpan(at + 8));
                string name2 = Encoding.ASCII.GetString(contents, at + 14, 4);
                uint offset = BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(at + 18));
                uint length = BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(at + 22));
                if (offset < contents.Length && length <= contents.Length - offset)
                {
                    chunks.Add(new QuillChunk(name, id, name2, (int)offset, (int)length));
                }
            }

            if (count == 0)
            {
                break;
            }

            block = next;
        }

        return chunks;
    }

    private static bool TryReadQuillText(byte[] contents, out IReadOnlyList<PublisherStory> stories)
    {
        stories = Array.Empty<PublisherStory>();
        if (contents.Length < 0x20 || Encoding.ASCII.GetString(contents, 2, Math.Min(8, contents.Length - 2)) != "CHNKINK ")
        {
            return false;
        }

        List<QuillChunk> chunks = ReadChunkIndex(contents);
        QuillChunk? text = chunks.FirstOrDefault(c => c.Name == "TEXT");
        if (text is not { Length: > 1 } t)
        {
            return false;
        }

        string all = Encoding.Unicode.GetString(contents, t.Offset, t.Length & ~1);

        // Story lengths (in UTF-16 characters) split the one long text into text boxes.
        var lengths = new List<int>();
        if (chunks.FirstOrDefault(c => c.Name == "STRS") is { Length: >= 12 } strs)
        {
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(strs.Offset));
            long sum = 0;
            for (int i = 0; i < count && strs.Offset + 12 + (i * 4) + 4 <= strs.Offset + strs.Length; i++)
            {
                uint len = BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(strs.Offset + 12 + (i * 4)));
                sum += len;
                lengths.Add((int)len);
            }

            if (sum > all.Length || lengths.Count == 0)
            {
                lengths.Clear();
            }
        }

        if (lengths.Count == 0)
        {
            lengths.Add(all.Length);
        }

        var result = new List<PublisherStory>();
        int start = 0;
        foreach (int length in lengths)
        {
            if (start + length > all.Length)
            {
                break;
            }

            IReadOnlyList<string> paragraphs = SplitParagraphs(all.AsSpan(start, length));
            start += length;
            if (paragraphs.Count > 0)
            {
                result.Add(new PublisherStory(result.Count + 1, paragraphs));
            }
        }

        stories = result;
        return true;
    }

    /// <summary>Paragraphs end at CR; a vertical tab is a line break inside one. Control codes (field marks, objects) are dropped.</summary>
    internal static IReadOnlyList<string> SplitParagraphs(ReadOnlySpan<char> text)
    {
        var paragraphs = new List<string>();
        var current = new StringBuilder();

        void Flush()
        {
            string p = current.ToString().Trim();
            if (p.Length > 0)
            {
                paragraphs.Add(p);
            }

            current.Clear();
        }

        foreach (char c in text)
        {
            switch (c)
            {
                case '\r':
                case '\n':
                case '\f':
                case '\u2029': // paragraph separator
                    Flush();
                    break;
                case '\v':
                case '\u2028': // line separator
                    current.Append('\n');
                    break;
                case '\t':
                case '\u0007': // table cell mark
                    current.Append('\t');
                    break;
                case '\u00A0': // no-break space
                    current.Append(' ');
                    break;
                case '\uFFFC': // object placeholder
                case '\uFEFF':
                    break;
                default:
                    if (!char.IsControl(c))
                    {
                        current.Append(c);
                    }

                    break;
            }
        }

        Flush();
        return paragraphs;
    }

    /// <summary>Recovers runs of readable UTF-16 text when the index cannot be used.</summary>
    internal static IReadOnlyList<PublisherStory> ScanText(byte[] bytes)
    {
        var runs = new List<string>();
        var run = new StringBuilder();

        void End()
        {
            string s = run.ToString();
            int letters = s.Count(char.IsLetterOrDigit);
            if (letters >= 3 && letters * 2 >= s.Trim().Length)
            {
                runs.Add(s);
            }

            run.Clear();
        }

        for (int i = 0; i + 1 < bytes.Length; i += 2)
        {
            char c = (char)(bytes[i] | (bytes[i + 1] << 8));
            bool readable = c is '\r' or '\n' or '\t' or '\v' ||
                            (c >= 0x20 && c < 0xD800 && !char.IsControl(c) && c != '\uFFFF' && c != '\uFFFE');
            if (readable)
            {
                run.Append(c);
            }
            else
            {
                End();
            }
        }

        End();

        var paragraphs = runs.SelectMany(r => SplitParagraphs(r)).Where(p => p.Length > 1).ToList();
        return paragraphs.Count == 0
            ? Array.Empty<PublisherStory>()
            : new[] { new PublisherStory(1, paragraphs) };
    }

    // ---- pictures -------------------------------------------------------------------

    private static List<PublisherImage> ExtractImages(Dictionary<string, byte[]> streams)
    {
        var images = new List<PublisherImage>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Escher streams first (that is where pictures live), then anything else.
        foreach ((string _, byte[] data) in streams.OrderBy(s => s.Key.StartsWith("Escher", StringComparison.OrdinalIgnoreCase) ? 0 : 1))
        {
            foreach ((string ext, byte[] bytes, int w, int h) in FindImages(data))
            {
                if (w < MinImageSide || h < MinImageSide || !seen.Add(Convert.ToHexString(SHA256.HashData(bytes))))
                {
                    continue;
                }

                images.Add(new PublisherImage(images.Count + 1, ext, bytes, w, h));
            }
        }

        return images;
    }

    internal static IEnumerable<(string Extension, byte[] Bytes, int Width, int Height)> FindImages(byte[] data)
    {
        for (int i = 0; i + 8 < data.Length; i++)
        {
            byte b = data[i];
            if (b == 0x89 && data.AsSpan(i).StartsWith(PngSignature) && TryPng(data, i, out int end, out int w, out int h))
            {
                yield return ("png", data[i..end], w, h);
                i = end - 1;
            }
            else if (b == 0xFF && data[i + 1] == 0xD8 && data[i + 2] == 0xFF && TryJpeg(data, i, out end, out w, out h))
            {
                yield return ("jpg", data[i..end], w, h);
                i = end - 1;
            }
            else if (b == 0x1F && data[i + 1] == 0xF0 && i >= 2 && TryDibRecord(data, i - 2, out byte[]? bmp, out int consumed, out w, out h))
            {
                yield return ("bmp", bmp!, w, h);
                i += consumed - 1;
            }
        }
    }

    /// <summary>A PNG ends with its IEND chunk; walking the chunks also proves it is a real PNG.</summary>
    private static bool TryPng(byte[] data, int start, out int end, out int width, out int height)
    {
        end = width = height = 0;
        int at = start + 8;
        bool header = false;
        while (at + 12 <= data.Length)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at));
            if (length > data.Length - at - 12)
            {
                return false;
            }

            string type = Encoding.ASCII.GetString(data, at + 4, 4);
            if (!header)
            {
                if (type != "IHDR" || length != 13)
                {
                    return false;
                }

                width = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 8));
                height = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 12));
                header = true;
            }

            at += 12 + (int)length;
            if (type == "IEND")
            {
                end = at;
                return width > 0 && height > 0;
            }
        }

        return false;
    }

    /// <summary>Walks JPEG markers to the frame header and the end-of-image marker after the scan data.</summary>
    private static bool TryJpeg(byte[] data, int start, out int end, out int width, out int height)
    {
        end = width = height = 0;
        int at = start + 2;
        while (at + 4 <= data.Length)
        {
            if (data[at] != 0xFF)
            {
                return false;
            }

            byte marker = data[at + 1];
            if (marker == 0xFF)
            {
                at++;
                continue;
            }

            if (marker == 0xD9)
            {
                end = at + 2;
                return width > 0 && height > 0;
            }

            int length = (data[at + 2] << 8) | data[at + 3];
            if (length < 2 || at + 2 + length > data.Length)
            {
                return false;
            }

            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC && length >= 7)
            {
                height = (data[at + 5] << 8) | data[at + 6];
                width = (data[at + 7] << 8) | data[at + 8];
            }

            at += 2 + length;
            if (marker == 0xDA)
            {
                // Entropy-coded data: FF 00 and restart markers belong to it; any other marker ends it.
                while (at + 1 < data.Length)
                {
                    if (data[at] == 0xFF && data[at + 1] != 0x00 && data[at + 1] is not (>= 0xD0 and <= 0xD7))
                    {
                        break;
                    }

                    at++;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// An Office drawing DIB record (type 0xF01F): header, one or two 16-byte ids, a tag
    /// byte, then a device-independent bitmap without its file header, which is added back.
    /// </summary>
    private static bool TryDibRecord(byte[] data, int start, out byte[]? bmp, out int consumed, out int width, out int height)
    {
        bmp = null;
        consumed = width = height = 0;
        if (start + 8 > data.Length)
        {
            return false;
        }

        ushort instance = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(start)) >> 4);
        uint recordLength = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(start + 4));
        if (instance is not (0x7A8 or 0x7A9) || recordLength < 57 || recordLength > data.Length - start - 8)
        {
            return false;
        }

        int dib = start + 8 + (instance == 0x7A9 ? 32 : 16) + 1;
        int dibLength = (int)recordLength - (dib - start - 8);
        if (dib + 40 > data.Length || BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(dib)) != 40)
        {
            return false;
        }

        width = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(dib + 4));
        height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(dib + 8)));
        ushort bits = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(dib + 14));
        uint colours = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(dib + 32));
        if (width <= 0 || height <= 0 || bits is not (1 or 4 or 8 or 16 or 24 or 32))
        {
            return false;
        }

        int palette = (int)(colours != 0 ? colours : bits <= 8 ? 1u << bits : 0u) * 4;
        var file = new byte[14 + dibLength];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(2), file.Length);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(10), 14 + 40 + palette);
        Buffer.BlockCopy(data, dib, file, 14, dibLength);
        bmp = file;
        consumed = 8 + (int)recordLength - 2;
        return true;
    }
}
