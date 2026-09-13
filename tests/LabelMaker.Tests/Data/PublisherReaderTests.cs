using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using LabelMaker.Data;
using LabelMaker.Data.Publisher;
using OpenMcdf;
using Xunit;

namespace LabelMaker.Tests.Data;

public sealed class PublisherReaderTests
{
    // ---- building a small file with Publisher's structure --------------------------

    private static byte[] QuillContents(params string[] stories)
    {
        string text = string.Concat(stories);
        byte[] textBytes = Encoding.Unicode.GetBytes(text);

        const int textOffset = 0x100;
        int strsOffset = textOffset + textBytes.Length;
        var contents = new byte[strsOffset + 12 + (stories.Length * 4)];

        Encoding.ASCII.GetBytes("CHNKINK ").CopyTo(contents, 2);
        BinaryPrimitives.WriteUInt16LittleEndian(contents.AsSpan(0x18 + 2), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(contents.AsSpan(0x18 + 4), 0xFFFFFFFF);

        void Entry(int at, string name, int offset, int length)
        {
            Encoding.ASCII.GetBytes(name).CopyTo(contents, at + 4);
            Encoding.ASCII.GetBytes(name).CopyTo(contents, at + 14);
            BinaryPrimitives.WriteUInt32LittleEndian(contents.AsSpan(at + 18), (uint)offset);
            BinaryPrimitives.WriteUInt32LittleEndian(contents.AsSpan(at + 22), (uint)length);
        }

        Entry(0x20, "TEXT", textOffset, textBytes.Length);
        Entry(0x20 + 26, "STRS", strsOffset, 12 + (stories.Length * 4));

        textBytes.CopyTo(contents, textOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(contents.AsSpan(strsOffset), (uint)stories.Length);
        for (int i = 0; i < stories.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(contents.AsSpan(strsOffset + 12 + (i * 4)), (uint)stories[i].Length);
        }

        return contents;
    }

    private static byte[] Png(int width, int height)
    {
        var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        void Chunk(string type, byte[] data)
        {
            Span<byte> len = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
            ms.Write(len);
            ms.Write(Encoding.ASCII.GetBytes(type));
            ms.Write(data);
            ms.Write(new byte[4]); // CRC is not checked
        }

        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
        ihdr[8] = 8;
        ihdr[9] = 2;
        Chunk("IHDR", ihdr);
        Chunk("IDAT", new byte[] { 0x78, 0x9C, 0x01, 0x02, 0x03 });
        Chunk("IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static byte[] Jpeg(int width, int height)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };
        bytes.AddRange(new byte[] { 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0 });
        bytes.AddRange(new byte[] { 0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1 });
        bytes.AddRange(new byte[] { 0xFF, 0xDA, 0x00, 0x0C, 3, 1, 0, 2, 0x11, 3, 0x11, 0, 0x3F, 0 });
        bytes.AddRange(new byte[] { 0x12, 0xFF, 0x00, 0x34, 0xFF, 0xD0, 0x56 }); // scan data with stuffed and restart markers
        bytes.AddRange(new byte[] { 0xFF, 0xD9 });
        return bytes.ToArray();
    }

    private static byte[] DibRecord(int width, int height)
    {
        int rowSize = ((width * 3) + 3) & ~3;
        var dib = new byte[40 + (rowSize * height)];
        BinaryPrimitives.WriteInt32LittleEndian(dib, 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), height);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 24);

        var record = new byte[8 + 16 + 1 + dib.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(record, 0x7A8 << 4);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(2), 0xF01F);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(4), (uint)(record.Length - 8));
        dib.CopyTo(record, 25);
        return record;
    }

    private static MemoryStream BuildPub(byte[]? quill, byte[] escher)
    {
        var ms = new MemoryStream();
        using (RootStorage root = RootStorage.Create(ms, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen))
        {
            if (quill is not null)
            {
                using CfbStream s = root.CreateStorage("Quill").CreateStorage("QuillSub").CreateStream("CONTENTS");
                s.Write(quill);
            }

            using (CfbStream contents = root.CreateStream("Contents"))
            {
                contents.Write(new byte[64]);
            }

            using CfbStream e = root.CreateStorage("Escher").CreateStream("EscherDelayStm");
            e.Write(escher);
        }

        ms.Position = 0;
        return ms;
    }

    private static byte[] Escher()
    {
        var ms = new MemoryStream();
        ms.Write(new byte[] { 0x00, 0x00, 0x1E, 0xF0, 0x40, 0x00, 0x00, 0x00 }); // a PNG BLIP header
        ms.Write(new byte[17]);
        ms.Write(Png(120, 80));
        ms.Write(new byte[] { 0xFF, 0xD8, 0x00, 0x11 }); // a false JPEG start that must be ignored
        ms.Write(Jpeg(640, 480));
        ms.Write(Png(120, 80)); // the same picture again is listed once
        ms.Write(DibRecord(6, 5));
        ms.Write(new byte[33]);
        return ms.ToArray();
    }

    // ---- tests ----------------------------------------------------------------------

    [Fact]
    public void Reads_each_text_box_as_a_story_with_its_paragraphs()
    {
        byte[] quill = QuillContents(
            "Fresh Apples\rOrganic\vGrade A\r",
            "Price:\u00A0£2.99\uFFFC PAGE\u0013 \r\r",
            "Tab\u0007separated\tcells\r");

        PublisherContent content = PublisherReader.Read(BuildPub(quill, Escher()));

        content.TextIsApproximate.Should().BeFalse();
        content.Stories.Should().HaveCount(3);
        content.Stories[0].Paragraphs.Should().Equal("Fresh Apples", "Organic\nGrade A");
        content.Stories[1].Paragraphs.Should().Equal("Price: £2.99 PAGE");
        content.Stories[2].Paragraphs.Should().Equal("Tab\tseparated\tcells");
    }

    [Fact]
    public void Finds_png_jpeg_and_bitmap_pictures_once_each()
    {
        PublisherContent content = PublisherReader.Read(BuildPub(QuillContents("x\r"), Escher()));

        content.Images.Select(i => (i.Extension, i.Width, i.Height)).Should().Equal(
            ("png", 120, 80),
            ("jpg", 640, 480),
            ("bmp", 6, 5));

        content.Images[0].Bytes.Should().Equal(Png(120, 80));
        content.Images[1].Bytes.Should().Equal(Jpeg(640, 480));
        content.Images[2].Bytes.AsSpan(0, 2).ToArray().Should().Equal((byte)'B', (byte)'M');
    }

    [Fact]
    public void Without_a_text_index_readable_text_is_recovered_and_flagged()
    {
        var quill = new byte[400];
        Encoding.Unicode.GetBytes("Summer Sale\rEverything must go\r").CopyTo(quill, 120);

        PublisherContent content = PublisherReader.Read(BuildPub(quill, new byte[16]));

        content.TextIsApproximate.Should().BeTrue();
        content.Stories.SelectMany(s => s.Paragraphs).Should().Contain(new[] { "Summer Sale", "Everything must go" });
        content.Notes.Should().NotBeEmpty();
    }

    [Fact]
    public void A_file_that_is_not_a_compound_document_is_refused()
    {
        Action act = () => PublisherReader.Read(new MemoryStream(Encoding.ASCII.GetBytes("just some text, not a publication")));

        act.Should().Throw<DataSourceException>();
    }

    [Fact]
    public void A_damaged_chunk_index_never_reads_outside_the_stream()
    {
        byte[] quill = QuillContents("Hello\r");
        BinaryPrimitives.WriteUInt32LittleEndian(quill.AsSpan(0x20 + 18), 0x7FFFFFF0); // TEXT offset past the end
        BinaryPrimitives.WriteUInt32LittleEndian(quill.AsSpan(0x18 + 4), 0x18);        // and a loop in the block list

        PublisherContent content = PublisherReader.Read(BuildPub(quill, new byte[8]));

        content.TextIsApproximate.Should().BeTrue();
        content.Stories.SelectMany(s => s.Paragraphs).Should().Contain(p => p.Contains("Hello"));
    }
}
