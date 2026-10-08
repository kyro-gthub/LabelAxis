using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using FluentAssertions;
using LabelMaker.App.ViewModels;
using LabelMaker.Core.Model;
using LabelMaker.Tests.Support;
using Xunit;

namespace LabelMaker.App.Tests.ViewModels;

/// <summary>
/// Every box in the Properties panel, for each kind of object: what is typed in is what the
/// box shows afterwards, and one undo puts the old value back.
/// </summary>
public sealed class PropertyBoxTests
{
    public static TheoryData<string, string, string> Boxes => new()
    {
        { "text", nameof(SelectionProperties.Name), "Price" },
        { "text", nameof(SelectionProperties.X), "7.5" },
        { "text", nameof(SelectionProperties.Y), "3.25" },
        { "text", nameof(SelectionProperties.Height), "9" },
        { "text", nameof(SelectionProperties.RotationDegrees), "15" },
        { "text", nameof(SelectionProperties.SkewX), "10" },
        { "text", nameof(SelectionProperties.SkewY), "-5" },
        { "text", nameof(SelectionProperties.Locked), "true" },
        { "text", nameof(SelectionProperties.Visible), "false" },
        { "text", nameof(SelectionProperties.FontSizePt), "14" },
        { "text", nameof(SelectionProperties.FontFamily), "Arial" },
        { "text", nameof(SelectionProperties.Italic), "true" },
        { "text", nameof(SelectionProperties.Underline), "true" },
        { "text", nameof(SelectionProperties.Strikethrough), "true" },
        { "text", nameof(SelectionProperties.TextColorHex), "#C80000" },
        { "text", nameof(SelectionProperties.HighlightHex), "#FFFF00" },
        { "text", nameof(SelectionProperties.TextAlign), "Center" },
        { "text", nameof(SelectionProperties.TextVerticalAlign), "Bottom" },
        { "text", nameof(SelectionProperties.TextDirectionValue), "RightToLeft" },
        { "text", nameof(SelectionProperties.AutoFit), "ShrinkToFit" },
        { "text", nameof(SelectionProperties.LineSpacing), "1.5" },
        { "text", nameof(SelectionProperties.LetterSpacingEm), "0.1" },
        { "text", nameof(SelectionProperties.TextPadding), "1.5" },
        { "barcode", nameof(SelectionProperties.BarcodeData), "XYZ123" },
        { "barcode", nameof(SelectionProperties.BarHeightMm), "18" },
        { "barcode", nameof(SelectionProperties.HriFontSizePt), "9" },
        { "barcode", nameof(SelectionProperties.ShowHumanReadable), "false" },
        { "barcode", nameof(SelectionProperties.HriPosition), "Above" },
        { "barcode", nameof(SelectionProperties.HriFontFamily), "Arial" },
        { "barcode", nameof(SelectionProperties.QuietZoneModules), "5" },
        { "barcode", nameof(SelectionProperties.BarColorHex), "#000080" },
        { "barcode", nameof(SelectionProperties.BarBackgroundHex), "#FFFFF0" },
        { "barcode", nameof(SelectionProperties.Symbology), "Code39" },
        { "qr", nameof(SelectionProperties.QrEcLevel), "High" },
        { "qr", nameof(SelectionProperties.CharacterEncoding), "ISO-8859-1" },
        { "shape", nameof(SelectionProperties.StrokeWidthMm), "0.8" },
        { "shape", nameof(SelectionProperties.StrokeHex), "#00A000" },
        { "shape", nameof(SelectionProperties.FillHex), "#336699" },
        { "shape", nameof(SelectionProperties.DashStyleValue), "Dash" },
        { "shape", nameof(SelectionProperties.ShapeKindValue), "Ellipse" },
        { "roundrect", nameof(SelectionProperties.CornerRadiusMm), "3" },
        { "table", nameof(SelectionProperties.TableHeader), "false" },
        { "table", nameof(SelectionProperties.TableHeaderFillHex), "#FFE0E0" },
        { "table", nameof(SelectionProperties.TableBorderHex), "#808080" },
        { "table", nameof(SelectionProperties.TableBorderWidth), "0.5" },
        { "table", nameof(SelectionProperties.TableFontFamily), "Arial" },
        { "table", nameof(SelectionProperties.TableFontSize), "10" },
        { "table", nameof(SelectionProperties.TableAlign), "Center" },
        { "table", nameof(SelectionProperties.TablePadding), "2" },
        { "image", nameof(SelectionProperties.FitMode), "Fill" },
        { "image", nameof(SelectionProperties.Brightness), "0.3" },
        { "image", nameof(SelectionProperties.Contrast), "0.2" },
        { "image", nameof(SelectionProperties.Grayscale), "true" },
        { "image", nameof(SelectionProperties.FlipHorizontal), "true" },
        { "image", nameof(SelectionProperties.FlipVertical), "true" },
        { "image", nameof(SelectionProperties.ImageOpacity), "0.5" },
        { "image", nameof(SelectionProperties.ClipShape), "Ellipse" },
        { "image", nameof(SelectionProperties.ImageHasBorder), "true" },
        { "image", nameof(SelectionProperties.ImageBorderLines), "Double" },
    };

    [Theory]
    [MemberData(nameof(Boxes))]
    public void A_typed_value_is_kept_and_one_undo_puts_the_old_one_back(string kind, string box, string typed) => AppHarness.Run(h =>
    {
        Add(h, kind);
        SelectionProperties panel = h.ViewModel.Properties;
        PropertyInfo property = typeof(SelectionProperties).GetProperty(box)!;
        object? before = property.GetValue(panel);
        object value = Convert(typed, property.PropertyType);

        property.SetValue(panel, value);

        Same(property.GetValue(panel), value).Should().BeTrue($"{box} shows what was typed");
        Same(before, value).Should().BeFalse($"the test must change {box}");
        h.Session.Undo();
        h.ViewModel.Properties.RefreshAll();
        Same(property.GetValue(panel), before).Should().BeTrue($"one undo brings {box} back");
    });

    private static void Add(AppHarness h, string kind)
    {
        switch (kind)
        {
            case "text":
                h.ViewModel.AddTextCommand.Execute(null);
                break;
            case "barcode":
                h.ViewModel.AddBarcodeCommand.Execute(null);
                break;
            case "qr":
                h.ViewModel.AddQrCodeCommand.Execute(null);
                break;
            case "shape":
                h.ViewModel.AddRectangleCommand.Execute(null);
                break;
            case "roundrect":
                h.ViewModel.AddRoundRectCommand.Execute(null);
                break;
            case "table":
                h.ViewModel.AddTableCommand.Execute(null);
                break;
            case "image":
                string png = h.FileIn("logo.png");
                File.WriteAllBytes(png, TestImages.Png(20, 10));
                h.Shell.OpenFileAnswer = png;
                h.ViewModel.AddImageCommand.Execute(null);
                break;
        }

        h.Session.Selection.Should().ContainSingle($"a {kind} was added");
    }

    private static object Convert(string typed, Type type) =>
        type == typeof(string) ? typed
        : type.IsEnum ? Enum.Parse(type, typed)
        : TypeDescriptor.GetConverter(type).ConvertFromString(null, CultureInfo.InvariantCulture, typed)!;

    private static bool Same(object? a, object? b) => (a, b) switch
    {
        (double x, double y) => Math.Abs(x - y) < 1e-6,
        (string x, string y) when x.StartsWith('#') && ColorRgba.TryParse(x, out ColorRgba cx) && ColorRgba.TryParse(y, out ColorRgba cy) => cx == cy,
        _ => Equals(a, b),
    };
}
