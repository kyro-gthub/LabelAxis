using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using LabelMaker.Core.Printing.Ipp;
using Xunit;

namespace LabelMaker.Tests.Printing;

public sealed class IppTests
{
    [Fact]
    public void A_request_encodes_to_the_bytes_the_protocol_expects()
    {
        byte[] bytes = IppMessage.Request(IppOperation.GetPrinterAttributes, "ipp://10.0.0.5/ipp/print", requestId: 7).Encode();

        bytes[..8].Should().Equal(2, 0, 0x00, 0x0B, 0, 0, 0, 7);
        bytes[8].Should().Be(IppTag.OperationGroup);
        bytes[9].Should().Be(IppTag.Charset);
        BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(10)).Should().Be((short)"attributes-charset".Length);
        bytes[^1].Should().Be(IppTag.EndOfAttributes);
        Encoding.UTF8.GetString(bytes).Should().Contain("ipp://10.0.0.5/ipp/print");
    }

    [Fact]
    public void A_printers_answer_decodes_with_multi_value_attributes_and_resolutions()
    {
        var answer = new IppMessage { Code = 0x0000, RequestId = 3 };
        answer.Add(IppTag.OperationGroup, IppTag.Charset, "attributes-charset", "utf-8");
        answer.Add(IppTag.PrinterGroup, IppTag.TextWithoutLanguage, "printer-make-and-model", "Brother HL-L3270CDW");
        answer.Add(IppTag.PrinterGroup, IppTag.MimeMediaType, "document-format-supported", "application/octet-stream", "image/pwg-raster", "application/pdf");
        answer.Add(IppTag.PrinterGroup, IppTag.Keyword, "pwg-raster-document-type-supported", "sgray_8", "srgb_8");
        answer.Add(IppTag.PrinterGroup, new IppAttribute(IppTag.Boolean, "color-supported", new[] { new byte[] { 1 } }));
        answer.Add(IppTag.PrinterGroup, new IppAttribute(IppTag.Resolution, "pwg-raster-document-resolution-supported", new[] { Res(300), Res(600) }));

        IppMessage back = IppMessage.Decode(answer.Encode());
        back.IsSuccess.Should().BeTrue();
        back.RequestId.Should().Be(3);

        IppPrinterInfo info = IppPrinterInfo.From("ipp://p/ipp/print", back);
        info.Model.Should().Be("Brother HL-L3270CDW");
        info.Formats.Should().Equal("application/octet-stream", "image/pwg-raster", "application/pdf");
        info.BestFormat.Should().Be(IppDocumentFormat.PwgRaster);
        info.RasterResolutions.Should().Equal(300, 600);
        info.RasterDpi.Should().Be(300);
        info.RasterInColor.Should().BeTrue();

        static byte[] Res(int dpi)
        {
            var b = new byte[9];
            BinaryPrimitives.WriteInt32BigEndian(b, dpi);
            BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(4), dpi);
            b[8] = 3;   // dots per inch
            return b;
        }
    }

    [Fact]
    public void A_pdf_only_printer_gets_pdf_and_a_grey_only_printer_gets_grey()
    {
        new IppPrinterInfo { Uri = "u", Formats = new[] { "application/pdf" } }.BestFormat.Should().Be(IppDocumentFormat.Pdf);
        new IppPrinterInfo { Uri = "u", Formats = new[] { "application/postscript" } }.BestFormat.Should().Be(IppDocumentFormat.None);
        new IppPrinterInfo { Uri = "u", ColorSupported = false, RasterTypes = new[] { "sgray_8", "srgb_8" } }.RasterInColor.Should().BeFalse();
        new IppPrinterInfo { Uri = "u", RasterResolutions = new[] { 360, 720 } }.RasterDpi.Should().Be(360);
    }

    [Fact]
    public void An_error_status_is_not_a_success_and_a_cut_off_answer_is_reported()
    {
        IppMessage.Decode(new IppMessage { Code = 0x040A }.Encode()).IsSuccess.Should().BeFalse();

        byte[] full = IppMessage.Request(IppOperation.PrintJob, "ipp://x/ipp/print").Encode();
        Action cut = () => IppMessage.Decode(full.AsSpan(0, full.Length - 6));
        cut.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Pwg_raster_round_trips_through_an_independent_decoder()
    {
        // 40 × 6 grey page: white rows, a row with a black bar and some noise, repeated rows.
        const int w = 40, h = 6;
        var pixels = new byte[w * h];
        Array.Fill(pixels, (byte)255);
        for (int x = 5; x < 25; x++)
        {
            pixels[(2 * w) + x] = 0;
            pixels[(3 * w) + x] = 0;
        }

        for (int x = 30; x < 40; x++)
        {
            pixels[(4 * w) + x] = (byte)(x * 7);
        }

        var page = new RasterPage(pixels, w, h, Color: false);
        using var ms = new MemoryStream();
        PwgRaster.Write(ms, new[] { page }, dpi: 300, PwgRaster.MediaName(210, 297));
        byte[] file = ms.ToArray();

        Encoding.ASCII.GetString(file, 0, 4).Should().Be("RaS2");
        ReadOnlySpan<byte> header = file.AsSpan(4, PwgRaster.HeaderSize);
        Encoding.ASCII.GetString(header[..9]).Should().Be("PwgRaster");
        U32(header, 276).Should().Be(300u);
        U32(header, 372).Should().Be((uint)w);
        U32(header, 376).Should().Be((uint)h);
        U32(header, 388).Should().Be(8u);
        U32(header, 392).Should().Be((uint)w);
        U32(header, 400).Should().Be(18u, "sGray");
        Encoding.ASCII.GetString(header.Slice(1732, 16)).Should().Be("iso_a4_210x297mm");

        Decode(file.AsSpan(4 + PwgRaster.HeaderSize), w, h, 1).Should().Equal(pixels);
        file.Length.Should().BeLessThan(4 + PwgRaster.HeaderSize + pixels.Length, "runs are compressed");
    }

    [Fact]
    public void Colour_raster_round_trips_too_with_long_runs_and_long_literal_stretches()
    {
        const int w = 300, h = 3;
        var pixels = new byte[w * h * 3];
        var random = new Random(4);
        random.NextBytes(pixels.AsSpan(0, w * 3));            // row 0: all different
        Array.Fill(pixels, (byte)200, w * 3, w * 3);          // row 1: one long run
        pixels.AsSpan(0, w * 3).CopyTo(pixels.AsSpan(w * 6)); // row 2: same as row 0

        using var ms = new MemoryStream();
        PwgRaster.WritePage(ms, new RasterPage(pixels, w, h, Color: true), 300, "iso_a4_210x297mm", totalPages: 1);

        Decode(ms.ToArray().AsSpan(PwgRaster.HeaderSize), w, h, 3).Should().Equal(pixels);
    }

    [Fact]
    public void Paper_sizes_get_their_pwg_names_whichever_way_round()
    {
        PwgRaster.MediaName(297, 210).Should().Be("iso_a4_210x297mm");
        PwgRaster.MediaName(215.9, 279.4).Should().Be("na_letter_8.5x11in");
        PwgRaster.MediaName(100, 150).Should().StartWith("custom_");
    }

    private static uint U32(ReadOnlySpan<byte> h, int offset) => BinaryPrimitives.ReadUInt32BigEndian(h[offset..]);

    /// <summary>Written from the PWG 5102.4 rules, separately from the encoder.</summary>
    private static byte[] Decode(ReadOnlySpan<byte> data, int width, int height, int bpp)
    {
        var output = new byte[width * height * bpp];
        int pos = 0, y = 0;
        while (y < height)
        {
            int repeat = data[pos++] + 1;
            var row = new byte[width * bpp];
            int x = 0;
            while (x < width)
            {
                int c = data[pos++];
                if (c <= 127)
                {
                    for (int i = 0; i <= c; i++)
                    {
                        data.Slice(pos, bpp).CopyTo(row.AsSpan((x + i) * bpp));
                    }

                    pos += bpp;
                    x += c + 1;
                }
                else
                {
                    int n = 257 - c;
                    data.Slice(pos, n * bpp).CopyTo(row.AsSpan(x * bpp));
                    pos += n * bpp;
                    x += n;
                }
            }

            for (int r = 0; r < repeat; r++)
            {
                row.CopyTo(output, (y + r) * width * bpp);
            }

            y += repeat;
        }

        pos.Should().Be(data.Length, "nothing is left over after the last row");
        return output;
    }
}
