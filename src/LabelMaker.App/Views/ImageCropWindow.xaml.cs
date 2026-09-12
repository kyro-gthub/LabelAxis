using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using LabelMaker.Core.Model.Objects;

namespace LabelMaker.App.Views;

/// <summary>
/// Interactive crop for an <see cref="ImageObject"/>. The result is the normalised
/// (0..1) rectangle the model already carries, so cropping is non-destructive —
/// the original pixels stay in the project's asset store.
/// </summary>
public partial class ImageCropWindow : Window
{
    private readonly ImageObject _image;

    // Crop in normalised image space.
    private Rect _crop = new(0, 0, 1, 1);

    private bool _dragging;
    private bool _moving;
    private Point _dragStart;
    private Rect _cropAtDragStart;
    private double _ratio;   // 0 = free

    public ImageCropWindow(ImageObject image, BitmapSource source)
    {
        _image = image;
        InitializeComponent();

        Preview.Source = source;
        _crop = new Rect(
            Math.Clamp(image.Adjustments.CropX, 0, 1),
            Math.Clamp(image.Adjustments.CropY, 0, 1),
            Math.Clamp(image.Adjustments.CropWidth, 0.01, 1),
            Math.Clamp(image.Adjustments.CropHeight, 0.01, 1));

        foreach (string r in new[] { "Free", "Original", "1 : 1", "4 : 3", "3 : 2", "16 : 9" })
        {
            RatioBox.Items.Add(r);
        }

        RatioBox.SelectedIndex = 0;
        Loaded += (_, _) => Redraw();
        SizeChanged += (_, _) => Redraw();
    }

    /// <summary>The crop the user settled on, in normalised image coordinates.</summary>
    public Rect Result => _crop;

    // ---- mapping between the displayed image and normalised image space ----

    /// <summary>Where the letterboxed image actually sits inside the stage, in device-independent pixels.</summary>
    private Rect ImageRect()
    {
        if (Preview.Source is not BitmapSource src || src.PixelWidth == 0 || src.PixelHeight == 0)
        {
            return Rect.Empty;
        }

        double availableW = Math.Max(1, Preview.ActualWidth);
        double availableH = Math.Max(1, Preview.ActualHeight);
        double scale = Math.Min(availableW / src.PixelWidth, availableH / src.PixelHeight);
        double w = src.PixelWidth * scale;
        double h = src.PixelHeight * scale;

        Point origin = Preview.TranslatePoint(new Point((availableW - w) / 2, (availableH - h) / 2), Stage);
        return new Rect(origin.X, origin.Y, w, h);
    }

    private Point ToNormalised(Point stagePoint)
    {
        Rect r = ImageRect();
        if (r.IsEmpty || r.Width <= 0 || r.Height <= 0)
        {
            return new Point(0, 0);
        }

        return new Point(
            Math.Clamp((stagePoint.X - r.X) / r.Width, 0, 1),
            Math.Clamp((stagePoint.Y - r.Y) / r.Height, 0, 1));
    }

    // ---- interaction ------------------------------------------------------

    private void OnStageDown(object sender, MouseButtonEventArgs e)
    {
        Point p = ToNormalised(e.GetPosition(Stage));
        _dragStart = p;
        _cropAtDragStart = _crop;
        _moving = _crop.Contains(p) && _crop.Width < 0.999;
        _dragging = true;
        Stage.CaptureMouse();
    }

    private void OnStageMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        Point p = ToNormalised(e.GetPosition(Stage));

        if (_moving)
        {
            double dx = Math.Clamp(p.X - _dragStart.X, -_cropAtDragStart.X, 1 - _cropAtDragStart.Right);
            double dy = Math.Clamp(p.Y - _dragStart.Y, -_cropAtDragStart.Y, 1 - _cropAtDragStart.Bottom);
            _crop = new Rect(_cropAtDragStart.X + dx, _cropAtDragStart.Y + dy, _cropAtDragStart.Width, _cropAtDragStart.Height);
        }
        else
        {
            double x = Math.Min(_dragStart.X, p.X);
            double y = Math.Min(_dragStart.Y, p.Y);
            double w = Math.Abs(p.X - _dragStart.X);
            double h = Math.Abs(p.Y - _dragStart.Y);

            if (_ratio > 0 && w > 0 && h > 0)
            {
                // Keep the requested aspect in *image* proportions, not screen ones.
                double pixelAspect = PixelAspect();
                double targetH = w / _ratio * pixelAspect;
                if (targetH <= 1)
                {
                    h = targetH;
                }
                else
                {
                    w = h * _ratio / pixelAspect;
                }

                if (p.X < _dragStart.X)
                {
                    x = Math.Max(0, _dragStart.X - w);
                }

                if (p.Y < _dragStart.Y)
                {
                    y = Math.Max(0, _dragStart.Y - h);
                }
            }

            if (w >= 0.02 && h >= 0.02)
            {
                _crop = new Rect(x, y, Math.Min(w, 1 - x), Math.Min(h, 1 - y));
            }
        }

        Redraw();
    }

    private void OnStageUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        Stage.ReleaseMouseCapture();
        Redraw();
    }

    private double PixelAspect()
    {
        if (Preview.Source is BitmapSource s && s.PixelHeight > 0)
        {
            return s.PixelWidth / (double)s.PixelHeight;
        }

        return 1;
    }

    private void OnRatioChanged(object sender, SelectionChangedEventArgs e)
    {
        _ratio = (RatioBox.SelectedItem as string) switch
        {
            "Original" => PixelAspect(),
            "1 : 1" => 1.0,
            "4 : 3" => 4.0 / 3.0,
            "3 : 2" => 3.0 / 2.0,
            "16 : 9" => 16.0 / 9.0,
            _ => 0,
        };
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        _crop = new Rect(0, 0, 1, 1);
        Redraw();
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        _image.Adjustments.CropX = Math.Round(_crop.X, 4);
        _image.Adjustments.CropY = Math.Round(_crop.Y, 4);
        _image.Adjustments.CropWidth = Math.Round(Math.Max(0.01, _crop.Width), 4);
        _image.Adjustments.CropHeight = Math.Round(Math.Max(0.01, _crop.Height), 4);
        DialogResult = true;
    }

    // ---- overlay ----------------------------------------------------------

    private void Redraw()
    {
        Overlay.Children.Clear();
        Rect img = ImageRect();
        if (img.IsEmpty || img.Width <= 0)
        {
            return;
        }

        var sel = new Rect(
            img.X + (_crop.X * img.Width),
            img.Y + (_crop.Y * img.Height),
            _crop.Width * img.Width,
            _crop.Height * img.Height);

        // Dim everything outside the selection with four bands (cheaper and crisper
        // than an even-odd geometry, and it never anti-aliases the selection edge).
        AddShade(img.X, img.Y, img.Width, sel.Y - img.Y);
        AddShade(img.X, sel.Bottom, img.Width, img.Bottom - sel.Bottom);
        AddShade(img.X, sel.Y, sel.X - img.X, sel.Height);
        AddShade(sel.Right, sel.Y, img.Right - sel.Right, sel.Height);

        var outline = new Rectangle
        {
            Width = Math.Max(0, sel.Width),
            Height = Math.Max(0, sel.Height),
            Stroke = (Brush)FindResource("Br.Accent"),
            StrokeThickness = 1.4,
        };
        Canvas.SetLeft(outline, sel.X);
        Canvas.SetTop(outline, sel.Y);
        Overlay.Children.Add(outline);

        foreach (Point corner in new[]
                 {
                     new Point(sel.X, sel.Y), new Point(sel.Right, sel.Y),
                     new Point(sel.X, sel.Bottom), new Point(sel.Right, sel.Bottom),
                 })
        {
            var dot = new Rectangle
            {
                Width = 8,
                Height = 8,
                Fill = Brushes.White,
                Stroke = (Brush)FindResource("Br.Accent"),
                StrokeThickness = 1.4,
            };
            Canvas.SetLeft(dot, corner.X - 4);
            Canvas.SetTop(dot, corner.Y - 4);
            Overlay.Children.Add(dot);
        }

        if (Preview.Source is BitmapSource src)
        {
            int px = (int)Math.Round(src.PixelWidth * _crop.Width);
            int py = (int)Math.Round(src.PixelHeight * _crop.Height);
            Readout.Text = $"{px} × {py} px  ({_crop.Width * 100:0}% × {_crop.Height * 100:0}%)";
        }
    }

    private void AddShade(double x, double y, double w, double h)
    {
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var band = new Rectangle
        {
            Width = w,
            Height = h,
            Fill = new SolidColorBrush(Color.FromArgb(0x99, 0x10, 0x16, 0x20)),
        };
        Canvas.SetLeft(band, x);
        Canvas.SetTop(band, y);
        Overlay.Children.Add(band);
    }
}
