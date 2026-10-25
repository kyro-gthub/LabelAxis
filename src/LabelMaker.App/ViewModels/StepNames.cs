namespace LabelMaker.App.ViewModels;

/// <summary>
/// The name an undo step gets when a setting in Properties changes, as the History tab and
/// the Edit menu show it: the words of the setting in the panel ("Font size"), not the name the
/// program knows it by. Each is translated like the rest of the program.
/// </summary>
public static class StepNames
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        ["AddCheckDigit"] = "Add check digit",
        ["AllDotsCurved"] = "Curved (smooth lines through the dots)",
        ["AllowStretch"] = "Allow stretching (ignore aspect ratio)",
        ["ArrowHeadSizeMm"] = "Arrow head size",
        ["AutoFit"] = "When it overflows",
        ["BarBackgroundHex"] = "Background",
        ["BarColorHex"] = "Bar colour",
        ["BarHeightMm"] = "Barcode height",
        ["BarcodeAutoSize"] = "Auto-size to symbol",
        ["BarcodeData"] = "Barcode data",
        ["Bold"] = "Bold",
        ["Brightness"] = "Brightness",
        ["CharacterEncoding"] = "Encoding",
        ["ClipCornerRadiusMm"] = "Clip radius",
        ["ClipShape"] = "Crop to shape",
        ["ClosedPath"] = "Closed shape (join the last dot to the first)",
        ["Contrast"] = "Contrast",
        ["CornerRadiusMm"] = "Corner radius",
        ["DashStyleValue"] = "Line style",
        ["DisplayUnit"] = "Units",
        ["FillAlpha"] = "Fill opacity",
        ["FillBrush"] = "Fill",
        ["FillHex"] = "Fill",
        ["FitMode"] = "Fit",
        ["FlipHorizontal"] = "Flip horizontal",
        ["FlipVertical"] = "Flip vertical",
        ["FontFamily"] = "Font",
        ["FontSizePt"] = "Font size",
        ["GradientAngle"] = "Angle",
        ["GradientPreview"] = "Gradient fill",
        ["GradientToHex"] = "To colour",
        ["Grayscale"] = "Grayscale",
        ["HighlightHex"] = "Highlight",
        ["HriFontFamily"] = "Digit font",
        ["HriFontSizePt"] = "Digits size",
        ["HriPosition"] = "Digits",
        ["ImageBorderDash"] = "Line style",
        ["ImageBorderHex"] = "Border",
        ["ImageBorderLines"] = "Lines",
        ["ImageBorderWidth"] = "Border width",
        ["ImageHasBorder"] = "Outline border",
        ["ImageOpacity"] = "Opacity",
        ["IsPolygon"] = "Shape",
        ["IsRoundRect"] = "Shape",
        ["IsRoundedClip"] = "Crop to shape",
        ["IsShrinkToFit"] = "When it overflows",
        ["Italic"] = "Italic",
        ["LetterSpacingEm"] = "Letter spacing",
        ["LineSpacing"] = "Line spacing",
        ["Locked"] = "Locked",
        ["MinAutoFitPt"] = "Smallest (pt)",
        ["ModuleWidthMm"] = "Module width",
        ["Name"] = "Name",
        ["PolygonPoints"] = "Points",
        ["QrEcLevel"] = "Error correction",
        ["QuietZoneModules"] = "Quiet zone",
        ["RotationDegrees"] = "Rotation",
        ["ShapeKindValue"] = "Shape",
        ["ShowHumanReadable"] = "Digits",
        ["SkewX"] = "Skew",
        ["SkewY"] = "Skew",
        ["StarInnerRatio"] = "Star depth",
        ["Strikethrough"] = "Strikethrough",
        ["StrokeBrush"] = "Stroke",
        ["StrokeHex"] = "Stroke",
        ["StrokeWidthMm"] = "Stroke width",
        ["Symbology"] = "Barcode Type",
        ["TableAlign"] = "Align",
        ["TableBorderHex"] = "Border",
        ["TableBorderWidth"] = "Border width",
        ["TableFontFamily"] = "Font",
        ["TableFontSize"] = "Text size (pt)",
        ["TableHeader"] = "First row is a header",
        ["TableHeaderFillHex"] = "Header fill",
        ["TablePadding"] = "Cell padding",
        ["TextColorHex"] = "Colour",
        ["TextContent"] = "Text",
        ["TextDirectionValue"] = "Direction",
        ["TextPadding"] = "Padding",
        ["TextVerticalAlign"] = "Vertical",
        ["Underline"] = "Underline",
        ["UnitSymbol"] = "Units",
        ["UseGradient"] = "Gradient fill",
        ["Visible"] = "Visible",
        ["WordWrap"] = "Wrap long lines",
        ["Fill"] = "Fill",
        ["Highlight"] = "Highlight",
    };

    /// <summary>The step's name for a change to <paramref name="property"/>; words already given pass through.</summary>
    public static string For(string property)
    {
        if (Names.TryGetValue(property, out string? name))
        {
            return name;
        }

        // Words given straight ("Reset crop") are a name already; a code name is spelled out.
        return property.Contains(' ', StringComparison.Ordinal) ? property : SpellOut(property);
    }

    /// <summary>All the step names, for checking they are translated.</summary>
    public static IEnumerable<string> All => Names.Values.Distinct();

    // "BarHeightMm" → "Bar height": a last resort for a setting added without a name here.
    private static string SpellOut(string property)
    {
        string trimmed = property.EndsWith("Mm", StringComparison.Ordinal) || property.EndsWith("Pt", StringComparison.Ordinal)
            ? property[..^2]
            : property;
        var sb = new System.Text.StringBuilder();
        foreach (char c in trimmed)
        {
            if (char.IsUpper(c) && sb.Length > 0)
            {
                sb.Append(' ').Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
