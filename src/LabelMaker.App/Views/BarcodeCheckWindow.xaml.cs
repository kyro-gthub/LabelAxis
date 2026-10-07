using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using LabelMaker.App.Localization;
using LabelMaker.App.Scanning;
using LabelMaker.Core.Barcodes.Gs1;
using LabelMaker.Core.Verification;
using Microsoft.Win32;

namespace LabelMaker.App.Views;

/// <summary>
/// Checks a printed barcode: from a webcam, live, or from a photo or scan. The barcode is read
/// and graded A to F with each measurement, says whether it matches a barcode on the label
/// being designed, and what to change when it prints poorly.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The camera's stop signal is disposed in StopCamera, which closing the window calls.")]
public partial class BarcodeCheckWindow : Window
{
    /// <summary>A row of the measurements list.</summary>
    private sealed record ParameterRow(string Name, string Value, string Meaning, string Letter, Brush Badge, bool HasGrade);

    private const int MaxPictureWidth = 2400;

    private readonly Func<IReadOnlyList<(string Name, string Value)>> _labelBarcodes;
    private CancellationTokenSource? _cameraStop;
    private Task? _cameraLoop;
    private WriteableBitmap? _preview;
    private volatile bool _checking;
    private volatile bool _previewPending;
    private DateTime _lastFound = DateTime.MinValue;
    private BarcodeQualityReport? _shown;

    /// <param name="labelBarcodes">The barcodes on the page being designed (name and value), to compare with.</param>
    public BarcodeCheckWindow(Func<IReadOnlyList<(string Name, string Value)>> labelBarcodes)
    {
        InitializeComponent();
        _labelBarcodes = labelBarcodes;
        Localizer.SetOff(ParameterList, true);
        Localizer.SetOff(AdviceList, true);

        IReadOnlyList<CameraInfo> cameras = Camera.List();
        CameraBox.ItemsSource = cameras;
        CameraBox.SelectedIndex = cameras.Count > 0 ? 0 : -1;
        if (cameras.Count == 0)
        {
            CameraBox.ItemsSource = new[] { Localizer.T("No camera found") };
            CameraBox.SelectedIndex = 0;
            CameraBox.IsEnabled = false;
            CameraButton.IsEnabled = false;
        }

        Closed += (_, _) => StopCamera();
    }

    // ---- camera ---------------------------------------------------------------------------

    private void OnCamera(object sender, RoutedEventArgs e)
    {
        if (_cameraStop is not null)
        {
            StopCamera();
            return;
        }

        if (CameraBox.SelectedItem is not CameraInfo camera)
        {
            return;
        }

        _cameraStop = new CancellationTokenSource();
        CancellationToken stop = _cameraStop.Token;
        CameraButton.Content = Localizer.T("Stop camera");
        EmptyText.Visibility = Visibility.Collapsed;
        // Media Foundation stays on the one thread that opened it, for the camera's whole life.
        _cameraLoop = Task.Factory.StartNew(() => RunCamera(camera, stop), stop, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private void RunCamera(CameraInfo info, CancellationToken stop)
    {
        try
        {
            using Camera camera = Camera.Open(info);
            while (!stop.IsCancellationRequested)
            {
                if (camera.Read() is not { } frame)
                {
                    break;
                }

                if (!_previewPending)
                {
                    _previewPending = true;
                    Dispatcher.BeginInvoke(() =>
                    {
                        _previewPending = false;
                        ShowFrame(frame);
                    });
                }

                if (!_checking)
                {
                    _checking = true;
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            BarcodeQualityReport report = BarcodeGrader.Check(GrayImage.FromBgra(frame.Bgra, frame.Width, frame.Height, frame.Stride));
                            Dispatcher.BeginInvoke(() => ShowReport(report, live: true));
                        }
                        finally
                        {
                            _checking = false;
                        }
                    }, CancellationToken.None);
                }
            }

            // The camera stopped sending pictures (unplugged, or taken by another program): back to the start state.
            if (!stop.IsCancellationRequested)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (_cameraStop is { } current && current.Token == stop)
                    {
                        StopCamera();
                    }
                });
            }
        }
        catch (InvalidOperationException ex)
        {
            Dispatcher.BeginInvoke(() =>
            {
                StopCamera();
                MessageBox.Show(this, Localizer.T(ex.Message), "LabelAxis", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }
    }

    private void StopCamera()
    {
        if (_cameraStop is null)
        {
            return;
        }

        _cameraStop.Cancel();
        _cameraStop.Dispose(); // the camera thread only reads the token's state, which stays readable
        _cameraStop = null;
        CameraButton.Content = Localizer.T("Start camera");
    }

    private void ShowFrame(CameraFrame frame)
    {
        if (_preview is null || _preview.PixelWidth != frame.Width || _preview.PixelHeight != frame.Height)
        {
            _preview = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null);
            PreviewImage.Source = _preview;
            Overlay.Width = frame.Width;
            Overlay.Height = frame.Height;
        }

        _preview.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Bgra, frame.Stride, 0);
    }

    // ---- pictures ---------------------------------------------------------------------------

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = Localizer.Filter("Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif;*.webp|All files|*.*"),
            Title = Localizer.T("Open a photo or scan of the label"),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            using FileStream stream = File.OpenRead(dialog.FileName);
            BitmapFrame picture = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            CheckPicture(picture);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, Localizer.T("The picture could not be opened:") + "\n\n" + ex.Message, "LabelAxis",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnPaste(object sender, RoutedEventArgs e)
    {
        if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } picture)
        {
            CheckPicture(picture);
        }
        else
        {
            MessageBox.Show(this, Localizer.T("There is no picture to paste. Copy one first (for example with the Snipping Tool)."), "LabelAxis",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    /// <summary>Checks a still picture (after stopping the camera), large photos made smaller first.</summary>
    private void CheckPicture(BitmapSource picture)
    {
        StopCamera();
        BitmapSource source = picture.PixelWidth > MaxPictureWidth
            ? new TransformedBitmap(picture, new ScaleTransform(MaxPictureWidth / (double)picture.PixelWidth, MaxPictureWidth / (double)picture.PixelWidth))
            : picture;
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width = bgra.PixelWidth, height = bgra.PixelHeight, stride = width * 4;
        var pixels = new byte[stride * height];
        bgra.CopyPixels(pixels, stride, 0);
        ShowFrame(new CameraFrame(pixels, width, height));
        EmptyText.Visibility = Visibility.Collapsed;
        GradeText.Text = Localizer.T("Checking…");
        HoldBox.IsChecked = false;
        Task.Run(() => BarcodeGrader.Check(GrayImage.FromBgra(pixels, width, height, stride)))
            .ContinueWith(t => Dispatcher.BeginInvoke(() => ShowReport(t.Result, live: false)), TaskScheduler.Default);
    }

    // ---- the result ---------------------------------------------------------------------------

    private static Brush GradeBrush(QualityGrade grade) => new SolidColorBrush(grade switch
    {
        QualityGrade.A => Color.FromRgb(0x1F, 0x9D, 0x55),
        QualityGrade.B => Color.FromRgb(0x5B, 0xA8, 0x2B),
        QualityGrade.C => Color.FromRgb(0xD9, 0x9A, 0x00),
        QualityGrade.D => Color.FromRgb(0xE3, 0x6A, 0x12),
        _ => Color.FromRgb(0xD2, 0x3B, 0x3B),
    });

    private void ShowReport(BarcodeQualityReport report, bool live)
    {
        if (live && (HoldBox.IsChecked == true || !IsLoaded))
        {
            return;
        }

        if (!report.Found)
        {
            // A moment without a barcode in a live picture keeps the last result on show.
            if (live && DateTime.Now - _lastFound < TimeSpan.FromSeconds(2))
            {
                return;
            }

            _shown = null;
            CopyButton.IsEnabled = false;
            GradeBadge.Background = (Brush)FindResource("Br.Surface2");
            GradeLetter.Text = "–";
            GradeText.Text = Localizer.T(live ? "Looking for a barcode…" : "No barcode found");
            KindText.Text = string.Empty;
            ValueText.Text = string.Empty;
            MatchText.Text = string.Empty;
            ParameterList.ItemsSource = null;
            ShowAdvice(report.Advice);
            Overlay.Children.Clear();
            return;
        }

        _lastFound = DateTime.Now;
        _shown = report;
        CopyButton.IsEnabled = true;
        GradeBadge.Background = GradeBrush(report.Grade);
        GradeLetter.Text = report.Grade.ToString();
        GradeText.Text = Localizer.F("Grade {0} ({1})", report.Grade, report.Score.ToString("0.0", CultureInfo.CurrentCulture)) + " · " +
                         Localizer.T(report.Grade switch
                         {
                             QualityGrade.A => "scans easily",
                             QualityGrade.B => "scans well",
                             QualityGrade.C => "scans, but could be better",
                             QualityGrade.D => "may not scan everywhere",
                             _ => "likely to fail",
                         });
        KindText.Text = report.Symbology;
        ValueText.Text = report.Text.Replace(Gs1.GroupSeparator, '·');
        ShowMatch(report);
        ParameterList.ItemsSource = report.Parameters.Select(p => new ParameterRow(
            Localizer.T(p.Name), Localizer.T(p.Value), Localizer.T(p.Meaning), p.Grade?.ToString() ?? string.Empty,
            p.Grade is { } g ? GradeBrush(g) : Brushes.Transparent, p.Grade is not null)).ToList();
        ShowAdvice(report.Advice);
        DrawOutline(report);
    }

    private void ShowAdvice(IEnumerable<string> advice)
    {
        AdviceList.Children.Clear();
        foreach (string line in advice)
        {
            var text = new TextBlock { Text = "•  " + Localizer.T(line), TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 0, 0, 6) };
            AdviceList.Children.Add(text);
        }
    }

    private void ShowMatch(BarcodeQualityReport report)
    {
        string scanned = Plain(report.Text);
        IReadOnlyList<(string Name, string Value)> barcodes = _labelBarcodes();
        (string Name, string Value)? match = barcodes.Cast<(string Name, string Value)?>()
            .FirstOrDefault(b => Same(Plain(b!.Value.Value), scanned));
        if (match is { } m)
        {
            MatchText.Text = "✓  " + Localizer.F("The same as “{0}” on this page.", string.IsNullOrWhiteSpace(m.Name) ? Localizer.T("the barcode") : m.Name);
            MatchText.SetResourceReference(TextBlock.ForegroundProperty, "Br.Ok");
        }
        else
        {
            MatchText.Text = barcodes.Count == 0 ? string.Empty : Localizer.T("Not the same as any barcode on this page.");
            MatchText.SetResourceReference(TextBlock.ForegroundProperty, "Br.TextMuted");
        }
    }

    /// <summary>A value without GS1 brackets and separators, for comparing.</summary>
    private static string Plain(string value)
    {
        string v = value.Trim().TrimStart(Gs1.GroupSeparator);
        if (v.StartsWith('(') && Gs1.Parse(v) is { Count: > 0 } elements)
        {
            v = Gs1.ElementString(elements);
        }

        return v.Replace(Gs1.GroupSeparator.ToString(), string.Empty, StringComparison.Ordinal);
    }

    // The same, also when one has the check digit the other leaves out (EAN, UPC).
    private static bool Same(string label, string scanned) =>
        string.Equals(label, scanned, StringComparison.Ordinal) ||
        (label.Length > 0 && label.All(char.IsAsciiDigit) && scanned.Length == label.Length + 1 && scanned.StartsWith(label, StringComparison.Ordinal)) ||
        (scanned.Length > 0 && scanned.TrimStart('0') == label.TrimStart('0') && label.All(char.IsAsciiDigit));

    private void DrawOutline(BarcodeQualityReport report)
    {
        Overlay.Children.Clear();
        if (report.Outline.Count < 2)
        {
            return;
        }

        double thickness = Math.Max(2, (Overlay.Width is > 0 and var w ? w : 800) / 300);
        var shape = new Polygon
        {
            Points = new PointCollection(report.Outline.Select(p => new Point(p.X, p.Y))),
            Stroke = GradeBrush(report.Grade),
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round,
        };
        Overlay.Children.Add(shape);
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (_shown is not { } r)
        {
            return;
        }

        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"{r.Symbology}: {r.Text.Replace(Gs1.GroupSeparator, '·')}");
        text.AppendLine(CultureInfo.InvariantCulture, $"{Localizer.F("Grade {0} ({1})", r.Grade, r.Score.ToString("0.0", CultureInfo.CurrentCulture))}");
        foreach (QualityParameter p in r.Parameters)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  {Localizer.T(p.Name)}: {Localizer.T(p.Value)}{(p.Grade is { } g ? "  " + g : string.Empty)}");
        }

        foreach (string a in r.Advice)
        {
            text.AppendLine("• " + Localizer.T(a));
        }

        Clipboard.SetText(text.ToString());
    }
}
