using System.Text;
using FluentAssertions;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Barcodes.Code128;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Printing.Thermal;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;
using LabelMaker.Core.Variables;
using Xunit;

namespace LabelMaker.Tests.Printing;

public sealed class NativeThermalTests
{
    private static readonly ThermalPrintSettings Settings = new() { Mode = ThermalSendMode.PrinterNative };

    private static TextObject Text(string content, double x = 5, double y = 5, double rotation = 0) => new()
    {
        Content = RichTextContent.FromPlainText(content),
        Bounds = new RectMm(x, y, 40, 8),
        FontSizePt = 12,
        RotationDegrees = rotation,
        AutoFit = TextAutoFit.None,
    };

    private static BarcodeObject Barcode(BarcodeSymbology symbology, string data, double x = 5, double y = 20) => new()
    {
        Spec = new BarcodeSpec { Symbology = symbology, Data = data, BarHeightMm = 10 },
        Bounds = new RectMm(x, y, 60, 15),
        AutoSize = true,
    };

    private static ResolveContext Record(params (string Field, string Value)[] values) => new()
    {
        Record = values.ToDictionary(v => v.Field, v => v.Value),
    };

    private static NativeLabelBuilder.Result Build(LabelDocument doc, ResolveContext context, INativeThermalEncoder? encoder = null) =>
        NativeLabelBuilder.Build(NativeDesign.For(doc), context, Settings, new ApproxTextMeasurer(), encoder ?? new ZplNativeEncoder(), 0, 0, 0);

    // ---- which objects go where -----------------------------------------------------------

    [Fact]
    public void Changing_text_and_barcodes_are_drawn_by_the_printer_and_fixed_parts_stay_in_the_background()
    {
        TextObject fixedText = Text("Best before");
        TextObject name = Text("{Name}");
        TextObject whiteName = Text("{Name}");
        whiteName.Foreground = ColorRgba.White;
        TextObject arabic = Text("{Name}");
        arabic.Direction = TextDirection.RightToLeft;
        TextObject slanted = Text("{Name}", rotation: 30);
        BarcodeObject fixedCode = Barcode(BarcodeSymbology.Code128, "ABC-123");
        BarcodeObject qr = Barcode(BarcodeSymbology.QrCode, "{Sku}");
        BarcodeObject aztec = Barcode(BarcodeSymbology.Aztec, "HELLO");
        var table = new TableObject { Cells = new List<string> { "{Name}", "x", "y", "z" } };
        var box = new ShapeObject();
        var doc = new LabelDocument { Objects = { fixedText, name, whiteName, arabic, slanted, fixedCode, qr, aztec, table, box } };

        NativeDesign design = NativeDesign.For(doc);

        design.Drawn.Should().Equal(name, fixedCode, qr);
        design.Pictures.Should().Equal(whiteName, arabic, slanted, table);
        design.BackgroundDocument().Objects.Should().Equal(fixedText, aztec, box);
    }

    [Fact]
    public void Each_label_carries_its_own_values()
    {
        var doc = new LabelDocument { Objects = { Text("{Name}"), Barcode(BarcodeSymbology.Ean13, "{Ean}") } };

        NativeLabelBuilder.Result first = Build(doc, Record(("Name", "Café crème"), ("Ean", "5901234123457")));
        NativeLabelBuilder.Result second = Build(doc, Record(("Name", "Tea"), ("Ean", "4006381333931")));

        first.NeedPictures.Should().BeEmpty();
        first.Label.Elements.OfType<NativeText>().Select(t => t.Text).Should().Contain("Café crème");
        first.Label.Elements.OfType<NativeBarcode>().Single().Data.Should().Be("590123412345", "the printer adds the check digit");
        second.Label.Elements.OfType<NativeText>().Select(t => t.Text).Should().Contain("Tea");
        second.Label.Elements.OfType<NativeBarcode>().Single().Data.Should().Be("400638133393");
    }

    [Fact]
    public void A_value_the_printer_font_has_no_letters_for_goes_as_a_picture_for_that_label_only()
    {
        TextObject name = Text("{Name}");
        var doc = new LabelDocument { Objects = { name } };

        Build(doc, Record(("Name", "Пример"))).NeedPictures.Select(p => p.Object).Should().Equal(name);
        Build(doc, Record(("Name", "Example"))).NeedPictures.Should().BeEmpty();
    }

    [Fact]
    public void Text_follows_lines_broken_as_on_screen_and_its_quarter_turn()
    {
        TextObject name = Text("{Name}", x: 10, y: 10, rotation: 90);
        name.Bounds = new RectMm(10, 10, 40, 20);
        name.WordWrap = true;
        var doc = new LabelDocument { Objects = { name } };

        var lines = Build(doc, Record(("Name", "A long product name that needs two lines at least"))).Label.Elements.OfType<NativeText>().ToList();

        lines.Should().HaveCountGreaterThan(1);
        lines.Should().OnlyContain(l => l.Rotation == NativeRotation.Quarter);
        // Turned a quarter, the 40 × 20 mm box stands 20 mm wide and 40 mm tall about its centre (30, 20).
        DotRect area = NativeLabelBuilder.Area(name, Settings);
        lines.Should().OnlyContain(l => l.Box.X >= area.X && l.Box.Right <= area.Right && l.Box.Y >= area.Y && l.Box.Bottom <= area.Bottom);
        lines.Should().OnlyContain(l => l.Box.Height > l.Box.Width, "each line runs down the label");
    }

    [Fact]
    public void Barcode_text_underneath_is_printer_text_and_the_bars_keep_their_module()
    {
        BarcodeObject code = Barcode(BarcodeSymbology.Code128, "{Sku}");
        code.Spec.ModuleWidthMm = 0.375; // 3 dots at 203 dpi
        var doc = new LabelDocument { Objects = { code } };

        List<NativeElement> elements = Build(doc, Record(("Sku", "LBL-00042"))).Label.Elements;

        NativeBarcode bars = elements.OfType<NativeBarcode>().Single();
        bars.ModuleDots.Should().Be(3);
        bars.BarHeightDots.Should().Be(Settings.Dots(10));
        bars.Code128Codewords.Should().Equal(Code128Encoder.Codewords("LBL-00042"));
        elements.OfType<NativeText>().Single().Text.Should().Be("LBL-00042");
        elements.OfType<NativeText>().Single().Box.Y.Should().BeGreaterThanOrEqualTo(bars.Box.Bottom - 1);
    }

    [Fact]
    public void Code39_gets_its_check_character_worked_out_here()
    {
        BarcodeObject code = Barcode(BarcodeSymbology.Code39, "CODE39");
        code.Spec.AddCheckDigit = true;

        Build(new LabelDocument { Objects = { code } }, new ResolveContext())
            .Label.Elements.OfType<NativeBarcode>().Single().Data.Should().Be("CODE39W");
    }

    // ---- Code 128 as each printer is given it --------------------------------------------

    [Theory]
    [InlineData("ABC123")]
    [InlineData("12345678")]
    [InlineData("Hello World 1234567890 end")]
    [InlineData("a1b2c3d4e5")]
    [InlineData("50%_off^now~")]
    public void Zpl_code128_gives_the_printer_exactly_our_code_values(string value)
    {
        List<int> codes = Code128Encoder.Codewords(value);
        string? zpl = ZplNativeEncoder.Code128Data(codes);

        zpl.Should().NotBeNull();
        ParseZpl(zpl!).Should().Equal(codes);
    }

    [Fact]
    public void Zpl_code128_carries_gs1_separators()
    {
        var spec = new BarcodeSpec { Symbology = BarcodeSymbology.GS1_128 };
        List<int> codes = BarcodeEngine.Code128Codewords(spec, "(01)09506000134352(10)ABC12(17)261231");

        string zpl = ZplNativeEncoder.Code128Data(codes)!;

        zpl.Should().StartWith(">;>8");
        ParseZpl(zpl).Should().Equal(codes);
    }

    [Fact]
    public void A_value_zpl_cannot_be_given_as_code128_goes_as_a_picture()
    {
        BarcodeObject code = Barcode(BarcodeSymbology.Code128, "{Sku}");
        var doc = new LabelDocument { Objects = { code } };

        ZplNativeEncoder.Code128Data(Code128Encoder.Codewords("A>B")).Should().BeNull();
        Build(doc, Record(("Sku", "A>B"))).NeedPictures.Select(p => p.Object).Should().Equal(code);
    }

    [Theory]
    [InlineData("ABC123")]
    [InlineData("12345678")]
    [InlineData("Hello World 1234567890 end")]
    public void Tspl_code128_gives_the_printer_exactly_our_code_values(string value)
    {
        List<int> codes = Code128Encoder.Codewords(value);
        string? tspl = TsplNativeEncoder.Code128Data(codes);

        tspl.Should().NotBeNull();
        ParseTspl(tspl!).Should().Equal(codes);
    }

    // ---- what goes down the wire -----------------------------------------------------------

    [Fact]
    public void Zpl_job_stores_the_background_once_and_each_row_recalls_it()
    {
        var background = new MonochromeImage(80, 40);
        background[3, 3] = true;
        var backgrounds = new List<MonochromeImage?> { background };
        var encoder = new ZplNativeEncoder();
        var row = new NativeRow { WidthDots = 80, HeightDots = 40 };
        var label = new NativeLabel { Background = 0 };
        label.Elements.Add(new NativeText(new DotRect(10, 10, 60, 20), 24, HorizontalAlignment.Center, NativeRotation.None, "50%_off^now~"));
        row.Labels.Add(label);

        string begin = Encoding.ASCII.GetString(encoder.BeginJob(backgrounds, Settings, new RollMedia(), 80, 5));
        string encoded = Encoding.ASCII.GetString(encoder.EncodeRow(row, backgrounds, Settings, new RollMedia(), 5, 2));
        string end = Encoding.ASCII.GetString(encoder.EndJob(backgrounds));

        begin.Should().StartWith("~DGR:LAXBG0.GRF,400,10,");
        encoded.Should().Contain("^FO0,0^XGR:LAXBG0.GRF,1,1^FS");
        encoded.Should().Contain("^FO10,10^A0N,24^FB60,1,0,C,0^FH_^FD50%_5Foff_5Enow_7E^FS");
        encoded.Should().Contain("^PQ2,0,1,N").And.NotContain("~DG");
        end.Should().Be("^XA^IDR:LAXBG0.GRF^FS^XZ\n");
    }

    [Fact]
    public void A_design_with_nothing_fixed_stores_nothing()
    {
        var backgrounds = new List<MonochromeImage?> { null };
        var row = new NativeRow { WidthDots = 80, HeightDots = 40 };
        row.Labels.Add(new NativeLabel { Background = -1 });
        var encoder = new ZplNativeEncoder();

        encoder.BeginJob(backgrounds, Settings, new RollMedia(), 80, 5).Should().BeEmpty();
        Encoding.ASCII.GetString(encoder.EncodeRow(row, backgrounds, Settings, new RollMedia(), 5, 1)).Should().NotContain("^XG");
        encoder.EndJob(backgrounds).Should().BeEmpty();
    }

    [Fact]
    public void Zpl_text_beyond_ascii_is_sent_as_utf8_hex()
    {
        var sb = new StringBuilder();
        ZplNativeEncoder.AppendField(sb, "Café");

        sb.ToString().Should().Be("Caf_C3_A9");
    }

    [Fact]
    public void Tspl_job_downloads_a_bmp_and_draws_fields_at_their_turned_reference_points()
    {
        var background = new MonochromeImage(20, 3);
        background[0, 0] = true;
        var backgrounds = new List<MonochromeImage?> { background };
        var encoder = new TsplNativeEncoder();
        var row = new NativeRow { WidthDots = 20, HeightDots = 3 };
        var label = new NativeLabel { Background = 0 };
        label.Elements.Add(new NativeText(new DotRect(10, 20, 30, 100), 24, HorizontalAlignment.Left, NativeRotation.Quarter, "Tea"));
        row.Labels.Add(label);

        byte[] begin = encoder.BeginJob(backgrounds, Settings, new RollMedia(), 20, 5);
        string head = Encoding.Latin1.GetString(begin);
        string encoded = Encoding.Latin1.GetString(encoder.EncodeRow(row, backgrounds, Settings, new RollMedia(), 5, 1));

        byte[] bmp = TsplNativeEncoder.Bmp(background);
        head.Should().Contain($"DOWNLOAD \"LAXBG0.BMP\",{bmp.Length},");
        // A quarter turn about the reference point: it sits at the box's top-right; the block runs 100 dots down, 30 across.
        encoded.Should().Contain("PUTBMP 0,0,\"LAXBG0.BMP\"").And.Contain("BLOCK 40,20,100,30,\"0\",90,9,9,0,1,\"Tea\"").And.EndWith("PRINT 1,1\r\n");
        Encoding.Latin1.GetString(encoder.EndJob(backgrounds)).Should().Be("KILL \"LAXBG0.BMP\"\r\n");
        encoder.CanCarry(new NativeText(new DotRect(0, 0, 1, 1), 10, HorizontalAlignment.Left, NativeRotation.None, "12\" pipe")).Should().BeFalse();
    }

    [Fact]
    public void Bmp_is_bottom_up_with_black_as_colour_zero()
    {
        var image = new MonochromeImage(20, 3);
        image[0, 0] = true; // top-left printed

        byte[] bmp = TsplNativeEncoder.Bmp(image);

        Encoding.ASCII.GetString(bmp, 0, 2).Should().Be("BM");
        BitConverter.ToInt32(bmp, 2).Should().Be(bmp.Length);
        BitConverter.ToInt32(bmp, 18).Should().Be(20);
        BitConverter.ToInt32(bmp, 22).Should().Be(3);
        const int pixels = 62, stride = 4;
        bmp[pixels + (2 * stride)].Should().Be(0x7F, "the top row is stored last; its first dot is black (0)");
        bmp[pixels].Should().Be(0xFF, "the bottom row is all white");
        bmp[pixels + 2].Should().Be(0xFF, "padding past the right edge stays white");
    }

    [Fact]
    public void Pictures_can_be_cut_out_cleared_and_laid_over()
    {
        var image = new MonochromeImage(16, 8);
        image[5, 5] = true;
        image[6, 5] = true;

        MonochromeImage part = image.Crop(4, 4, 4, 4)!;
        part.Width.Should().Be(4);
        part[1, 1].Should().BeTrue();
        part[2, 1].Should().BeTrue();
        image.Crop(20, 20, 4, 4).Should().BeNull();

        image.Clear(5, 5, 1, 1);
        image[5, 5].Should().BeFalse();
        image[6, 5].Should().BeTrue();

        var target = new MonochromeImage(16, 8);
        target.Overlay(part, 10, 2);
        target[11, 3].Should().BeTrue();
    }

    // ---- helpers: read the printer's Code 128 back into code values ------------------------

    private static List<int> ParseZpl(string data)
    {
        var codes = new List<int>();
        char set = 'B';
        bool shifted = false;
        string field = System.Text.RegularExpressions.Regex.Replace(data, "_([0-9A-F]{2})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
        for (int i = 0; i < field.Length; i++)
        {
            if (field[i] == '>')
            {
                int v = field[++i] + 46; // >1 … >; are 95 … 105
                codes.Add(v);
                set = v switch
                {
                    103 or 101 when set != 'A' => 'A',
                    104 or 100 => 'B',
                    105 or 99 => 'C',
                    _ => set,
                };
                shifted = v == 98;
                continue;
            }

            if (set == 'C')
            {
                codes.Add(int.Parse(field.AsSpan(i, 2)));
                i++;
                continue;
            }

            char current = shifted ? (set == 'A' ? 'B' : 'A') : set;
            shifted = false;
            codes.Add(current == 'A' && field[i] < ' ' ? field[i] + 64 : field[i] - 32);
        }

        return codes;
    }

    private static List<int> ParseTspl(string data)
    {
        var codes = new List<int>();
        char set = 'B';
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] == '!')
            {
                int v = int.Parse(data.AsSpan(i + 1, 3));
                i += 3;
                codes.Add(v);
                set = v switch
                {
                    103 or 101 => 'A',
                    104 or 100 => 'B',
                    105 or 99 => 'C',
                    _ => set,
                };
                continue;
            }

            if (set == 'C')
            {
                codes.Add(int.Parse(data.AsSpan(i, 2)));
                i++;
                continue;
            }

            codes.Add(data[i] - 32);
        }

        return codes;
    }
}
