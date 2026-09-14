using System.Globalization;
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
///
/// Drag a corner or side handle to resize the crop, drag inside it to move it, and drag
/// on the dimmed area outside it to draw a new one. Exact pixel values can be typed.
/// Only a drag that starts outside the crop replaces it: grabbing an edge used to start a
/// brand-new selection, which is why adjusting the crop kept resetting its size.
/// </summary>
public partial class ImageCropWindow : Window
{
    private const double HandleHitPx = 9;
    private const double MinSize = 0.02;

    private enum Grip
    {
        None,
        New,
        Move,
        Left,
        Top,
        Right,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }

    private readonly ImageObject _image;

    // Crop in normalised image space.
    private Rect _crop = new(0, 0, 1, 1);

    private Grip _grip = Grip.None;
    private Point _dragStart;
    private Rect _cropAtDragStart;
    private double _ratio;   // 0 = free, otherwise width / height in pixels
    private bool _updatingFields;
    private bool _committing;

    public ImageCropWindow(ImageObject image, BitmapSource source)
    {
        _image = image;
        InitializeComponent();

        Preview.Source = source;
        _crop = Clamp(new Rect(image.Adjustments.CropX, image.Adjustments.CropY, image.Adjustments.CropWidth, image.Adjustments.CropHeight));

        foreach (string r in new[] { "Free", "Original", "1 : 1", "4 : 3", "3 : 4", "3 : 2", "2 : 3", "16 : 9", "9 : 16" })
        {
            RatioBox.Items.Add(r);
        }

        RatioBox.SelectedIndex = 0;
        foreach (TextBox box in new[] { XBox, YBox, WBox, HBox })
        {
            box.LostFocus += (_, _) => OnFieldsCommitted();
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    OnFieldsCommitted();
                    e.Handled = true;
                }
            };
        }

        Loaded += (_, _) => Redraw();
        SizeChanged += (_, _) => Redraw();
        Preview.SizeChanged += (_, _) => Redraw();
    }

    /// <summary>The crop the user settled on, in normalised image coordinates.</summary>
    public Rect Result => _crop;

    private int PixelWidth => Preview.Source is BitmapSource s ? s.PixelWidth : 1;

    private int PixelHeight => Preview.Source is BitmapSource s ? s.PixelHeight : 1;

    /// <summary>Width over height of the whole image in pixels.</summary>
    private double ImageAspect => PixelHeight > 0 ? PixelWidth / (double)PixelHeight : 1;

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

    private Point ToNormalised(Point stagePoint, bool clamp = true)
    {
        Rect r = ImageRect();
        if (r.IsEmpty || r.Width <= 0 || r.Height <= 0)
        {
            return new Point(0, 0);
        }

        double x = (stagePoint.X - r.X) / r.Width;
        double y = (stagePoint.Y - r.Y) / r.Height;
        return clamp ? new Point(Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1)) : new Point(x, y);
    }

    private Rect SelectionOnStage()
    {
        Rect img = ImageRect();
        return img.IsEmpty
            ? Rect.Empty
            : new Rect(img.X + (_crop.X * img.Width), img.Y + (_crop.Y * img.Height), _crop.Width * img.Width, _crop.Height * img.Height);
    }

    /// <summary>What a press at <paramref name="p"/> (stage pixels) would grab.</summary>
    private Grip HitTest(Point p)
    {
        Rect sel = SelectionOnStage();
        if (sel.IsEmpty)
        {
            return Grip.None;
        }

        bool nearLeft = Math.Abs(p.X - sel.Left) <= HandleHitPx;
        bool nearRight = Math.Abs(p.X - sel.Right) <= HandleHitPx;
        bool nearTop = Math.Abs(p.Y - sel.Top) <= HandleHitPx;
        bool nearBottom = Math.Abs(p.Y - sel.Bottom) <= HandleHitPx;
        bool withinX = p.X >= sel.Left - HandleHitPx && p.X <= sel.Right + HandleHitPx;
        bool withinY = p.Y >= sel.Top - HandleHitPx && p.Y <= sel.Bottom + HandleHitPx;

        if (nearTop && nearLeft)
        {
            return Grip.TopLeft;
        }

        if (nearTop && nearRight)
        {
            return Grip.TopRight;
        }

        if (nearBottom && nearLeft)
        {
            return Grip.BottomLeft;
        }

        if (nearBottom && nearRight)
        {
            return Grip.BottomRight;
        }

        if (nearLeft && withinY)
        {
            return Grip.Left;
        }

        if (nearRight && withinY)
        {
            return Grip.Right;
        }

        if (nearTop && withinX)
        {
            return Grip.Top;
        }

        if (nearBottom && withinX)
        {
            return Grip.Bottom;
        }

        return sel.Contains(p) ? Grip.Move : Grip.New;
    }

    private static Cursor CursorFor(Grip grip) => grip switch
    {
        Grip.TopLeft or Grip.BottomRight => Cursors.SizeNWSE,
        Grip.TopRight or Grip.BottomLeft => Cursors.SizeNESW,
        Grip.Left or Grip.Right => Cursors.SizeWE,
        Grip.Top or Grip.Bottom => Cursors.SizeNS,
        Grip.Move => Cursors.SizeAll,
        _ => Cursors.Cross,
    };

    // ---- interaction ------------------------------------------------------

    private void OnStageDown(object sender, MouseButtonEventArgs e)
    {
        Point stage = e.GetPosition(Stage);
        _grip = HitTest(stage);
        if (_grip == Grip.None)
        {
            return;
        }

        _dragStart = ToNormalised(stage, clamp: _grip == Grip.New);
        _cropAtDragStart = _crop;
        Stage.CaptureMouse();
        e.Handled = true;
    }

    private void OnStageMove(object sender, MouseEventArgs e)
    {
        Point stage = e.GetPosition(Stage);
        if (_grip == Grip.None)
        {
            Stage.Cursor = CursorFor(HitTest(stage));
            return;
        }

        Point p = ToNormalised(stage, clamp: _grip == Grip.New);
        Rect s = _cropAtDragStart;
        double dx = p.X - _dragStart.X;
        double dy = p.Y - _dragStart.Y;

        switch (_grip)
        {
            case Grip.Move:
                dx = Math.Clamp(dx, -s.X, 1 - s.Right);
                dy = Math.Clamp(dy, -s.Y, 1 - s.Bottom);
                _crop = new Rect(s.X + dx, s.Y + dy, s.Width, s.Height);
                break;

            case Grip.New:
                if (Math.Abs(dx) < MinSize && Math.Abs(dy) < MinSize)
                {
                    return; // a click, not a drag: keep the current crop
                }

                _crop = Constrain(FromCorners(_dragStart, p), anchorX: dx >= 0 ? _dragStart.X : null, anchorY: dy >= 0 ? _dragStart.Y : null,
                    fromRight: dx < 0, fromBottom: dy < 0, fixedRight: _dragStart.X, fixedBottom: _dragStart.Y);
                break;

            default:
                _crop = ResizeFromGrip(s, _grip, dx, dy);
                break;
        }

        Redraw();
    }

    private void OnStageUp(object sender, MouseButtonEventArgs e)
    {
        _grip = Grip.None;
        Stage.ReleaseMouseCapture();
        Redraw();
    }

    private static Rect FromCorners(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));

    /// <summary>Moves the grabbed edges; the opposite edges stay where they were.</summary>
    private Rect ResizeFromGrip(Rect s, Grip grip, double dx, double dy)
    {
        double left = s.Left, top = s.Top, right = s.Right, bottom = s.Bottom;
        bool l = grip is Grip.Left or Grip.TopLeft or Grip.BottomLeft;
        bool r = grip is Grip.Right or Grip.TopRight or Grip.BottomRight;
        bool t = grip is Grip.Top or Grip.TopLeft or Grip.TopRight;
        bool b = grip is Grip.Bottom or Grip.BottomLeft or Grip.BottomRight;

        if (l)
        {
            left = Math.Clamp(left + dx, 0, right - MinSize);
        }

        if (r)
        {
            right = Math.Clamp(right + dx, left + MinSize, 1);
        }

        if (t)
        {
            top = Math.Clamp(top + dy, 0, bottom - MinSize);
        }

        if (b)
        {
            bottom = Math.Clamp(bottom + dy, top + MinSize, 1);
        }

        var resized = new Rect(left, top, right - left, bottom - top);
        if (_ratio <= 0)
        {
            return resized;
        }

        // Keep the chosen shape. A side handle sets one dimension and the other follows about
        // the crop's middle; a corner keeps its opposite corner fixed.
        bool sideOnly = grip is Grip.Left or Grip.Right or Grip.Top or Grip.Bottom;
        if (sideOnly)
        {
            bool widthLeads = grip is Grip.Left or Grip.Right;
            double w = widthLeads ? resized.Width : HeightToWidth(resized.Height);
            double h = widthLeads ? WidthToHeight(resized.Width) : resized.Height;
            (w, h) = FitInside(w, h);
            double cx = widthLeads ? (l ? right - w : left) : s.X + (s.Width / 2) - (w / 2);
            double cy = widthLeads ? s.Y + (s.Height / 2) - (h / 2) : (t ? bottom - h : top);
            return Clamp(new Rect(cx, cy, w, h));
        }

        return Constrain(resized, anchorX: l ? null : left, anchorY: t ? null : top,
            fromRight: l, fromBottom: t, fixedRight: right, fixedBottom: bottom);
    }

    /// <summary>Applies the aspect ratio to a box grown from a fixed corner.</summary>
    private Rect Constrain(Rect box, double? anchorX, double? anchorY, bool fromRight, bool fromBottom, double fixedRight, double fixedBottom)
    {
        if (_ratio <= 0)
        {
            return Clamp(box);
        }

        double w = Math.Max(MinSize, box.Width);
        double h = WidthToHeight(w);
        if (h < box.Height)
        {
            h = Math.Max(MinSize, box.Height);
            w = HeightToWidth(h);
        }

        // Stay inside the image on the side the box grows towards.
        double roomX = fromRight ? fixedRight : 1 - (anchorX ?? box.X);
        double roomY = fromBottom ? fixedBottom : 1 - (anchorY ?? box.Y);
        if (w > roomX)
        {
            w = roomX;
            h = WidthToHeight(w);
        }

        if (h > roomY)
        {
            h = roomY;
            w = HeightToWidth(h);
        }

        double x = fromRight ? fixedRight - w : anchorX ?? box.X;
        double y = fromBottom ? fixedBottom - h : anchorY ?? box.Y;
        return Clamp(new Rect(x, y, w, h));
    }

    /// <summary>Normalised height for a normalised width at the chosen pixel ratio.</summary>
    private double WidthToHeight(double w) => w * ImageAspect / _ratio;

    private double HeightToWidth(double h) => h * _ratio / ImageAspect;

    private (double W, double H) FitInside(double w, double h)
    {
        double scale = Math.Min(1, Math.Min(1 / Math.Max(w, 1e-9), 1 / Math.Max(h, 1e-9)));
        return (w * scale, h * scale);
    }

    private static Rect Clamp(Rect r)
    {
        double w = Math.Clamp(double.IsFinite(r.Width) ? r.Width : 1, MinSize, 1);
        double h = Math.Clamp(double.IsFinite(r.Height) ? r.Height : 1, MinSize, 1);
        double x = Math.Clamp(double.IsFinite(r.X) ? r.X : 0, 0, 1 - w);
        double y = Math.Clamp(double.IsFinite(r.Y) ? r.Y : 0, 0, 1 - h);
        return new Rect(x, y, w, h);
    }

    private void OnRatioChanged(object sender, SelectionChangedEventArgs e)
    {
        _ratio = (RatioBox.SelectedItem as string) switch
        {
            "Original" => ImageAspect,
            "1 : 1" => 1.0,
            "4 : 3" => 4.0 / 3.0,
            "3 : 4" => 3.0 / 4.0,
            "3 : 2" => 3.0 / 2.0,
            "2 : 3" => 2.0 / 3.0,
            "16 : 9" => 16.0 / 9.0,
            "9 : 16" => 9.0 / 16.0,
            _ => 0,
        };

        if (_ratio > 0 && IsLoaded)
        {
            // Show the new shape straight away: the largest box of that shape inside the
            // current crop, centred on it.
            double w = _crop.Width;
            double h = WidthToHeight(w);
            if (h > _crop.Height)
            {
                h = _crop.Height;
                w = HeightToWidth(h);
            }

            _crop = Clamp(new Rect(_crop.X + ((_crop.Width - w) / 2), _crop.Y + ((_crop.Height - h) / 2), w, h));
            Redraw();
        }
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        RatioBox.SelectedIndex = 0;
        _crop = new Rect(0, 0, 1, 1);
        Redraw();
    }

    /// <summary>Typed pixel values: position and size of the crop in the original image.</summary>
    private void OnFieldsCommitted()
    {
        if (_updatingFields)
        {
            return;
        }

        static double? Read(TextBox box) =>
            double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double v) ||
            double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                ? v
                : null;

        double pw = PixelWidth, ph = PixelHeight;
        double x = (Read(XBox) ?? (_crop.X * pw)) / pw;
        double y = (Read(YBox) ?? (_crop.Y * ph)) / ph;
        double w = (Read(WBox) ?? (_crop.Width * pw)) / pw;
        double h = (Read(HBox) ?? (_crop.Height * ph)) / ph;
        if (_ratio > 0)
        {
            h = WidthToHeight(w);
        }

        _crop = Clamp(new Rect(x, y, Math.Min(w, 1), Math.Min(h, 1)));
        _committing = true;
        Redraw();
        _committing = false;
    }

    /// <summary>
    /// Only reports the chosen rectangle. The caller applies it as an undoable
    /// command, so the dialog never edits the document behind the history's back.
    /// </summary>
    private void OnApply(object sender, RoutedEventArgs e)
    {
        OnFieldsCommitted();
        _crop = new Rect(
            Math.Round(_crop.X, 4),
            Math.Round(_crop.Y, 4),
            Math.Round(Math.Max(0.01, _crop.Width), 4),
            Math.Round(Math.Max(0.01, _crop.Height), 4));
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

        Rect sel = SelectionOnStage();
        var accent = (Brush)FindResource("Br.Accent");

        // Dim everything outside the selection with four bands (cheaper and crisper
        // than an even-odd geometry, and it never anti-aliases the selection edge).
        AddShade(img.X, img.Y, img.Width, sel.Y - img.Y);
        AddShade(img.X, sel.Bottom, img.Width, img.Bottom - sel.Bottom);
        AddShade(img.X, sel.Y, sel.X - img.X, sel.Height);
        AddShade(sel.Right, sel.Y, img.Right - sel.Right, sel.Height);

        // Rule-of-thirds guides help frame the picture.
        var thirds = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF));
        for (int i = 1; i <= 2; i++)
        {
            AddLine(sel.X + (sel.Width * i / 3), sel.Y, sel.X + (sel.Width * i / 3), sel.Bottom, thirds);
            AddLine(sel.X, sel.Y + (sel.Height * i / 3), sel.Right, sel.Y + (sel.Height * i / 3), thirds);
        }

        var outline = new Rectangle
        {
            Width = Math.Max(0, sel.Width),
            Height = Math.Max(0, sel.Height),
            Stroke = accent,
            StrokeThickness = 1.5,
        };
        Canvas.SetLeft(outline, sel.X);
        Canvas.SetTop(outline, sel.Y);
        Overlay.Children.Add(outline);

        double cx = sel.X + (sel.Width / 2), cy = sel.Y + (sel.Height / 2);
        foreach (Point handle in new[]
                 {
                     new Point(sel.X, sel.Y), new Point(cx, sel.Y), new Point(sel.Right, sel.Y),
                     new Point(sel.Right, cy), new Point(sel.Right, sel.Bottom), new Point(cx, sel.Bottom),
                     new Point(sel.X, sel.Bottom), new Point(sel.X, cy),
                 })
        {
            var dot = new Rectangle
            {
                Width = 10,
                Height = 10,
                Fill = Brushes.White,
                Stroke = accent,
                StrokeThickness = 1.5,
            };
            Canvas.SetLeft(dot, handle.X - 5);
            Canvas.SetTop(dot, handle.Y - 5);
            Overlay.Children.Add(dot);
        }

        int px = (int)Math.Round(PixelWidth * _crop.Width);
        int py = (int)Math.Round(PixelHeight * _crop.Height);
        Readout.Text = $"{px} × {py} px  ({_crop.Width * 100:0}% × {_crop.Height * 100:0}%)";

        // Every box shows the settled value, except one still being typed into.
        _updatingFields = true;
        foreach ((TextBox box, double value) in new[]
                 {
                     (XBox, Math.Round(_crop.X * PixelWidth)), (YBox, Math.Round(_crop.Y * PixelHeight)),
                     (WBox, (double)px), (HBox, (double)py),
                 })
        {
            if (!box.IsKeyboardFocusWithin || _grip != Grip.None || _committing)
            {
                box.Text = value.ToString(CultureInfo.CurrentCulture);
            }
        }

        _updatingFields = false;
    }

    private void AddLine(double x1, double y1, double x2, double y2, Brush brush) =>
        Overlay.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = brush, StrokeThickness = 1 });

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
