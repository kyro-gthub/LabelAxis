using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using LabelMaker.App.Mvvm;
using LabelMaker.Core.Model;

namespace LabelMaker.App.Controls;

/// <summary>
/// A compact swatch grid in the style of office apps: a row of neutrals, a row of clear
/// colours, then lighter tints and deeper shades of each. The swatch matching
/// <see cref="Hex"/> is ringed; clicking one sets <see cref="Hex"/>, which the properties
/// panel binds two-way to the colour being edited.
/// </summary>
public sealed class ColorPalette : Control
{
    public const int Columns = 10;

    /// <summary>(hex, name) in rows of <see cref="Columns"/>.</summary>
    public static readonly IReadOnlyList<(string Hex, string Name)> Colors = new (string, string)[]
    {
        ("#00000000", "No colour"), ("#FFFFFF", "White"), ("#F2F2F2", "Grey 5%"), ("#D9D9D9", "Grey 15%"), ("#BFBFBF", "Grey 25%"),
        ("#8C8C8C", "Grey 45%"), ("#595959", "Grey 65%"), ("#404040", "Grey 75%"), ("#262626", "Grey 85%"), ("#000000", "Black"),

        ("#E53935", "Red"), ("#FB8C00", "Orange"), ("#FDD835", "Yellow"), ("#43A047", "Green"), ("#00897B", "Teal"),
        ("#00ACC1", "Cyan"), ("#1E88E5", "Blue"), ("#3949AB", "Indigo"), ("#8E24AA", "Purple"), ("#D81B60", "Pink"),

        ("#FFCDD2", "Light red"), ("#FFE0B2", "Light orange"), ("#FFF9C4", "Light yellow"), ("#C8E6C9", "Light green"), ("#B2DFDB", "Light teal"),
        ("#B2EBF2", "Light cyan"), ("#BBDEFB", "Light blue"), ("#C5CAE9", "Light indigo"), ("#E1BEE7", "Light purple"), ("#F8BBD0", "Light pink"),

        ("#B71C1C", "Dark red"), ("#E65100", "Dark orange"), ("#F9A825", "Dark yellow"), ("#1B5E20", "Dark green"), ("#004D40", "Dark teal"),
        ("#006064", "Dark cyan"), ("#0D47A1", "Dark blue"), ("#1A237E", "Dark indigo"), ("#4A148C", "Dark purple"), ("#880E4F", "Dark pink"),
    };

    public static readonly DependencyProperty HexProperty = DependencyProperty.Register(
        nameof(Hex), typeof(string), typeof(ColorPalette),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((ColorPalette)d).UpdateRings()));

    private const double Cell = 18;
    private const double Gap = 3;

    private static readonly HexBrushConverter BrushOf = new();

    private readonly List<(Border Ring, string Hex)> _swatches = new();

    public ColorPalette()
    {
        Focusable = false;
        var grid = new UniformGrid { Columns = Columns, HorizontalAlignment = HorizontalAlignment.Left };
        foreach ((string hex, string name) in Colors)
        {
            var fill = new Border
            {
                CornerRadius = new CornerRadius(3),
                Background = (Brush)BrushOf.Convert(hex, typeof(Brush), null, System.Globalization.CultureInfo.InvariantCulture),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x2A, 0, 0, 0)),
                BorderThickness = new Thickness(1),
            };
            var ring = new Border
            {
                Width = Cell,
                Height = Cell,
                Margin = new Thickness(0, 0, Gap, Gap),
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Child = fill,
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                ToolTip = hex == "#00000000" ? name : $"{name}  {hex}",
            };
            System.Windows.Automation.AutomationProperties.SetName(ring, name);
            ring.MouseEnter += (_, _) => Hover(ring, hex, true);
            ring.MouseLeave += (_, _) => Hover(ring, hex, false);
            ring.MouseLeftButtonUp += (_, e) =>
            {
                Hex = hex;
                e.Handled = true;
            };
            _swatches.Add((ring, hex));
            grid.Children.Add(ring);
        }

        AddVisualChild(grid);
        _root = grid;
    }

    private readonly UniformGrid _root;

    public string? Hex
    {
        get => (string?)GetValue(HexProperty);
        set => SetValue(HexProperty, value);
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _root;

    protected override Size MeasureOverride(Size constraint)
    {
        _root.Measure(constraint);
        return _root.DesiredSize;
    }

    protected override Size ArrangeOverride(Size arrangeBounds)
    {
        _root.Arrange(new Rect(arrangeBounds));
        return arrangeBounds;
    }

    private void Hover(Border ring, string hex, bool over)
    {
        if (Same(hex, Hex))
        {
            return;
        }

        ring.BorderThickness = new Thickness(over ? 1.5 : 0);
        ring.Padding = new Thickness(over ? 1.5 : 0);
        if (over)
        {
            ring.SetResourceReference(Border.BorderBrushProperty, "Br.TextMuted");
        }
        else
        {
            ring.ClearValue(Border.BorderBrushProperty);
        }
    }

    private void UpdateRings()
    {
        foreach ((Border ring, string hex) in _swatches)
        {
            bool current = Same(hex, Hex);
            ring.BorderThickness = new Thickness(current ? 2 : 0);
            ring.Padding = new Thickness(current ? 1.5 : 0);
            if (current)
            {
                ring.SetResourceReference(Border.BorderBrushProperty, "Br.Accent");
            }
            else
            {
                ring.ClearValue(Border.BorderBrushProperty);
            }
        }
    }

    /// <summary>"#RRGGBB" and "#RRGGBBFF" are the same colour; any fully transparent colour matches "No colour".</summary>
    private static bool Same(string a, string? b)
    {
        if (b is null || !ColorRgba.TryParse(a, out ColorRgba x) || !ColorRgba.TryParse(b, out ColorRgba y))
        {
            return false;
        }

        return (x.A == 0 && y.A == 0) || x == y;
    }
}
