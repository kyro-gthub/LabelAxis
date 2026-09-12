using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using LabelMaker.App.ViewModels;
using LabelMaker.Core.Model;

namespace LabelMaker.App.Views;

/// <summary>
/// Colour chooser: palette, RGBA sliders, hex entry and an eyedropper that reads a
/// pixel from anywhere on screen. Home-grown rather than the WinForms ColorDialog
/// so it follows the app's theme and supports alpha.
/// </summary>
public partial class ColorPickerWindow : Window
{
    private bool _suppress;

    public ColorPickerWindow(ColorRgba initial, string title)
    {
        InitializeComponent();
        Title = title;

        Checker.Fill = BuildCheckerBrush();
        BuildPalette();

        _suppress = true;
        RSlider.Value = initial.R;
        GSlider.Value = initial.G;
        BSlider.Value = initial.B;
        ASlider.Value = initial.A;
        _suppress = false;
        Sync();
    }

    public ColorRgba Result { get; private set; }

    private static Brush BuildCheckerBrush()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 12, 12))));
        var dark = new SolidColorBrush(Color.FromRgb(0xDD, 0xE2, 0xEA));
        group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(0, 0, 6, 6))));
        group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(6, 6, 6, 6))));
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 12, 12),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
    }

    private void BuildPalette()
    {
        foreach (string hex in SelectionProperties.Swatches)
        {
            if (!ColorRgba.TryParse(hex, out ColorRgba c))
            {
                continue;
            }

            var cell = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(7),
                Margin = new Thickness(0, 0, 6, 6),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)FindResource("Br.BorderStrong"),
                Background = c.A == 0
                    ? BuildCheckerBrush()
                    : new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B)),
                Cursor = Cursors.Hand,
                ToolTip = hex,
            };

            cell.MouseLeftButtonUp += (_, _) =>
            {
                _suppress = true;
                RSlider.Value = c.R;
                GSlider.Value = c.G;
                BSlider.Value = c.B;
                ASlider.Value = c.A;
                _suppress = false;
                Sync();
            };

            Palette.Items.Add(cell);
        }
    }

    private ColorRgba Current => new(
        (byte)RSlider.Value, (byte)GSlider.Value, (byte)BSlider.Value, (byte)ASlider.Value);

    private void Sync()
    {
        ColorRgba c = Current;
        Preview.Fill = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
        HexBox.Text = c.ToHex();
        Result = c;
    }

    private void OnChannel(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_suppress && IsLoaded)
        {
            Sync();
        }
    }

    private void OnHexCommitted(object sender, RoutedEventArgs e)
    {
        if (ColorRgba.TryParse(HexBox.Text, out ColorRgba c))
        {
            _suppress = true;
            RSlider.Value = c.R;
            GSlider.Value = c.G;
            BSlider.Value = c.B;
            ASlider.Value = c.A;
            _suppress = false;
        }

        Sync();
    }

    private void OnTransparent(object sender, RoutedEventArgs e)
    {
        Result = ColorRgba.Transparent;
        DialogResult = true;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Sync();
        DialogResult = true;
    }

    // ---- eyedropper -------------------------------------------------------

    private void OnPickFromScreen(object sender, RoutedEventArgs e)
    {
        Mouse.OverrideCursor = Cursors.Cross;
        CaptureMouse();
        MouseLeftButtonDown += PickOnce;
    }

    private void PickOnce(object sender, MouseButtonEventArgs e)
    {
        MouseLeftButtonDown -= PickOnce;
        ReleaseMouseCapture();
        Mouse.OverrideCursor = null;

        if (!GetCursorPos(out NativePoint p))
        {
            return;
        }

        IntPtr dc = GetDC(IntPtr.Zero);
        try
        {
            uint bgr = GetPixel(dc, p.X, p.Y);
            _suppress = true;
            RSlider.Value = bgr & 0xFF;
            GSlider.Value = (bgr >> 8) & 0xFF;
            BSlider.Value = (bgr >> 16) & 0xFF;
            _suppress = false;
            Sync();
        }
        finally
        {
            _ = ReleaseDC(IntPtr.Zero, dc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr dc, int x, int y);
}
