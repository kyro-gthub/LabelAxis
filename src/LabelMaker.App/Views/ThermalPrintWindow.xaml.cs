using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LabelMaker.App.Printing;
using LabelMaker.App.Rendering;
using LabelMaker.App.Services;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Printing;
using LabelMaker.Core.Printing.Thermal;
using Microsoft.Win32;

namespace LabelMaker.App.Views;

/// <summary>
/// Prints to a thermal label printer by sending its own command language (ZPL or TSPL),
/// whatever driver is (or isn't) installed. Each label goes either as a 1-bit picture at the
/// print head's resolution, so what prints is exactly what the preview shows, dot for dot, or
/// in printer-native mode as the printer's own text and barcode commands over a background
/// stored on the printer once (see <see cref="NativeDesign"/>).
/// </summary>
public partial class ThermalPrintWindow : Window
{
    private readonly DocumentSession _session;
    private readonly SheetVisualRenderer _renderer;
    private readonly WpfPrintServer _printServer;
    private readonly IShellServices _shell;
    private readonly PrintHistoryStore _history;
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private ThermalJobPlan? _plan;
    private int _previewIndex;
    private bool _loading = true;
    private bool _planStale = true;

    public ThermalPrintWindow(DocumentSession session, SheetVisualRenderer renderer, WpfPrintServer printServer, IShellServices shell, PrintHistoryStore history)
    {
        _session = session;
        _renderer = renderer;
        _printServer = printServer;
        _shell = shell;
        _history = history;
        InitializeComponent();

        foreach (string printer in printServer.InstalledPrinters())
        {
            PrinterBox.Items.Add(printer);
        }

        foreach (int baud in new[] { 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200 })
        {
            BaudBox.Items.Add(baud);
        }

        LoadPorts(Print.Thermal.SerialPortName);

        LoadSettings();
        _refresh.Tick += (_, _) =>
        {
            _refresh.Stop();
            Refresh();
        };
        Closed += (_, _) =>
        {
            _refresh.Stop();
            _stopFinding?.Invoke();
        };
        _loading = false;
        Loaded += (_, _) => Refresh();
    }

    /// <summary>True when the user asked to print through the Windows driver instead.</summary>
    public bool UseDriverInstead { get; private set; }

    private PrintSettings Print => _session.Project.PrintSettings;

    private ThermalPrintSettings Thermal => Print.Thermal;

    // ---- settings <-> form ------------------------------------------------------------

    private void LoadSettings()
    {
        ThermalPrintSettings t = Thermal;
        LanguageBox.SelectedIndex = (int)t.Language;
        ModeBox.SelectedIndex = (int)t.Mode;
        DestinationBox.SelectedIndex = (int)t.Destination;
        string? printer = t.PrinterName ?? PrinterBox.Items.OfType<string>().FirstOrDefault(IsLikelyLabelPrinter) ?? _printServer.DefaultPrinter();
        PrinterBox.SelectedItem = PrinterBox.Items.OfType<string>().FirstOrDefault(p => p == printer) ?? PrinterBox.Items.OfType<object>().FirstOrDefault();
        AddressBox.Text = t.NetworkAddress;
        BaudBox.SelectedItem = BaudBox.Items.OfType<int>().Contains(t.BaudRate) ? t.BaudRate : 9600;
        DpiBox.SelectedItem = DpiBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == t.Dpi.ToString(CultureInfo.InvariantCulture)) ?? DpiBox.Items[0];

        RollMedia roll = _session.Project.Template?.Roll ?? new RollMedia();
        SensingBox.SelectedIndex = (int)(t.Sensing ?? roll.Sensing);
        AfterBox.SelectedIndex = (int)t.AfterPrint;
        OffsetXBox.Text = t.OffsetXMm.ToString("0.##", CultureInfo.InvariantCulture);
        OffsetYBox.Text = t.OffsetYMm.ToString("0.##", CultureInfo.InvariantCulture);
        RotateBox.IsChecked = t.Rotate180;
        DarknessSlider.Value = t.Darkness;
        SpeedSlider.Value = t.SpeedIps;
        DitherBox.SelectedIndex = (int)t.Dither;
        ThresholdSlider.Value = t.Threshold;
        CopiesBox.Text = Math.Max(1, Print.Copies).ToString(CultureInfo.InvariantCulture);
        FirstRowBox.Text = Print.FirstSheet?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        LastRowBox.Text = Print.LastSheet?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        UpdateRows();
    }

    private static readonly string[] LabelPrinterWords =
        { "zebra", "zdesigner", "tsc", "xprinter", "godex", "honeywell", "intermec", "citizen", "sato", "bixolon", "label" };

    private static bool IsLikelyLabelPrinter(string name) =>
        LabelPrinterWords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));

    private void Apply()
    {
        ThermalPrintSettings t = Thermal;
        t.Language = (ThermalLanguage)Math.Max(0, LanguageBox.SelectedIndex);
        t.Mode = (ThermalSendMode)Math.Max(0, ModeBox.SelectedIndex);
        t.Destination = (ThermalDestination)Math.Max(0, DestinationBox.SelectedIndex);
        t.PrinterName = PrinterBox.SelectedItem as string;
        t.NetworkAddress = AddressBox.Text.Trim();
        t.SerialPortName = SelectedPortName();
        t.BaudRate = BaudBox.SelectedItem is int baud ? baud : 9600;
        t.Dpi = DpiBox.SelectedItem is ComboBoxItem { Tag: string dpi } ? int.Parse(dpi, CultureInfo.InvariantCulture) : 203;

        var sensing = (MediaSensing)Math.Max(0, SensingBox.SelectedIndex);
        t.Sensing = sensing == (_session.Project.Template?.Roll?.Sensing ?? MediaSensing.Gap) ? null : sensing;
        t.AfterPrint = (ThermalAfterPrint)Math.Max(0, AfterBox.SelectedIndex);
        t.OffsetXMm = Math.Clamp(ParseDouble(OffsetXBox.Text, 0), -20, 20);
        t.OffsetYMm = Math.Clamp(ParseDouble(OffsetYBox.Text, 0), -20, 20);
        t.Rotate180 = RotateBox.IsChecked == true;
        t.Darkness = (int)DarknessSlider.Value;
        t.SpeedIps = (int)SpeedSlider.Value;
        t.Dither = (DitherMode)Math.Max(0, DitherBox.SelectedIndex);
        t.Threshold = (int)ThresholdSlider.Value;

        int? first = ParseRow(FirstRowBox.Text), last = ParseRow(LastRowBox.Text);
        if (first != Print.FirstSheet || last != Print.LastSheet)
        {
            Print.FirstSheet = first;
            Print.LastSheet = last;
            _planStale = true;
        }

        Print.Copies = Math.Clamp((int)ParseDouble(CopiesBox.Text, 1), 1, 9999);
        if (Print.Content == PrintContent.VariableData && _session.RecordCount == 0)
        {
            Print.Content = PrintContent.SheetFill;
            _planStale = true;
        }
    }

    private void UpdateRows()
    {
        ThermalDestination destination = (ThermalDestination)Math.Max(0, DestinationBox.SelectedIndex);
        PrinterRow.Visibility = destination == ThermalDestination.WindowsPrinter ? Visibility.Visible : Visibility.Collapsed;
        AddressRow.Visibility = destination == ThermalDestination.Network ? Visibility.Visible : Visibility.Collapsed;
        SerialRows.Visibility = destination == ThermalDestination.SerialPort ? Visibility.Visible : Visibility.Collapsed;
        if (destination != ThermalDestination.Network)
        {
            FoundRow.Visibility = Visibility.Collapsed;
        }
        ThresholdRow.Visibility = DitherBox.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        PrintButtonText.Text = destination == ThermalDestination.File ? "Save…" : "Print";
    }

    // ---- refresh ----------------------------------------------------------------------

    private void OnChanged(object sender, SelectionChangedEventArgs e) => Changed();

    private void OnDestinationChanged(object sender, SelectionChangedEventArgs e) => Changed();

    private void OnTextChanged(object sender, TextChangedEventArgs e) => Changed();

    private void OnClickChanged(object sender, RoutedEventArgs e) => Changed();

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => Changed();

    private void Changed()
    {
        if (_loading)
        {
            return;
        }

        UpdateRows();
        _refresh.Stop();
        _refresh.Start();
    }

    private void Refresh()
    {
        Apply();
        LabelTemplate? template = _session.Project.Template;
        try
        {
            if (_plan is null || _planStale)
            {
                _plan = ThermalJobPlan.Build(_session.Project, _session.Data, Print, _renderer);
                _planStale = false;
                _previewIndex = Math.Clamp(_previewIndex, 0, Math.Max(0, _plan.Count - 1));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            SummaryText.Text = ex.Message;
            PrintButton.IsEnabled = false;
            return;
        }

        LabelTemplate roll = _plan.Roll;
        HeaderSubtitle.Text = template is { IsRoll: true }
            ? $"{roll.Name} · {roll.Grid.Columns} across · roll {roll.EffectivePage.WidthMm:0.##} mm wide"
            : $"Designed on a sheet: prints one {roll.Grid.LabelWidthMm:0.##} × {roll.Grid.LabelHeightMm:0.##} mm label at a time";

        ContentText.Text = Print.Content switch
        {
            PrintContent.VariableData when Print.SelectedRecords.Count > 0 => $"One label for each of {Print.SelectedRecords.Count:N0} chosen records.",
            PrintContent.VariableData => $"One label per record — {_session.RecordCount:N0} record(s).",
            PrintContent.Pages => "The project's standalone pages.",
            _ => "The master label.",
        };

        if (Thermal.Mode == ThermalSendMode.PrinterNative)
        {
            (int drawn, int pictures) = _plan.NativeSummary();
            // Sentence by sentence: joined, they could match a shorter translated pattern.
            ModeHint.Text = Localization.Localizer.T(
                                "The fixed parts are stored on the printer once; each label then sends only its changing text and barcodes, " +
                                "which the printer draws with its own fonts, so text looks a little different from the screen.") + " " +
                            Localization.Localizer.F("Here the printer draws {0} item(s) itself and {1} go as small pictures.", drawn, pictures);
        }
        else
        {
            ModeHint.Text = Localization.Localizer.T("Each label goes to the printer as one picture, so it prints exactly as on screen.");
        }

        int copies = Math.Max(1, Print.Copies);
        SummaryText.Text = $"{_plan.Count:N0} row(s) · {_plan.Summary.Labels:N0} label(s)" +
                           (copies > 1 ? $" × {copies} copies" : string.Empty) +
                           (_plan.Summary.TotalSheets != _plan.Count ? $" (rows {_plan.Summary.FirstSheet:N0}+ of {_plan.Summary.TotalSheets:N0})" : string.Empty);
        PrintButton.IsEnabled = _plan.Count > 0;
        ShowPreview();
    }

    private void ShowPreview()
    {
        if (_plan is null || _plan.Count == 0)
        {
            PreviewImage.Source = null;
            PositionText.Text = "—";
            PreviewCaption.Text = "Nothing to print with these settings.";
            return;
        }

        MonochromeImage image = _plan.Rasterize(_previewIndex, Thermal);
        if (Thermal.Rotate180)
        {
            image = image.Rotated180();
        }

        var bitmap = new WriteableBitmap(image.Width, image.Height, 96, 96, PixelFormats.BlackWhite, null);
        var inverted = new byte[image.Bits.Length];
        for (int i = 0; i < inverted.Length; i++)
        {
            inverted[i] = (byte)~image.Bits[i]; // BlackWhite: 1 = white
        }

        bitmap.WritePixels(new Int32Rect(0, 0, image.Width, image.Height), inverted, image.BytesPerRow, 0);
        bitmap.Freeze();
        PreviewImage.Source = bitmap;

        double scale = Math.Min(560.0 / image.Width, 440.0 / image.Height);
        PreviewImage.Width = image.Width * Math.Max(0.2, Math.Min(scale, 3));
        PreviewImage.Height = image.Height * Math.Max(0.2, Math.Min(scale, 3));

        PositionText.Text = $"Row {_previewIndex + 1:N0} of {_plan.Count:N0}";
        PreviewCaption.Text = $"{image.Width} × {image.Height} dots at {Thermal.Dpi} dpi, {image.Coverage:P0} printed" +
                              (image.Coverage > 0.6 ? " — very dark, which wears the print head; check the design." : ".") +
                              (Thermal.Rotate180 ? " Shown as it comes out of the printer (upside down)." : string.Empty);
    }

    private void OnPrevious(object sender, RoutedEventArgs e) => Step(-1);

    private void OnNext(object sender, RoutedEventArgs e) => Step(+1);

    private void Step(int direction)
    {
        if (_plan is null || _plan.Count == 0)
        {
            return;
        }

        _previewIndex = (_previewIndex + direction + _plan.Count) % _plan.Count;
        ShowPreview();
    }

    // ---- actions ------------------------------------------------------------------------

    private void OnAdvanced(object sender, RoutedEventArgs e)
    {
        Apply();
        _shell.ShowPrintSettings();
        _planStale = true;
        LoadSettings();
        Refresh();
    }

    private void OnDriver(object sender, RoutedEventArgs e)
    {
        Apply();
        UseDriverInstead = true;
        DialogResult = false;
    }

    // ---- Bluetooth / serial ports ------------------------------------------------------

    /// <summary>Lists the COM ports, Bluetooth printers first, keeping <paramref name="wanted"/> selected.</summary>
    private void LoadPorts(string? wanted)
    {
        SerialPortBox.Items.Clear();
        IReadOnlyList<SerialPortInfo> ports = SerialPrinters.List();
        foreach (SerialPortInfo port in ports)
        {
            SerialPortBox.Items.Add(port);
        }

        SerialPortInfo? match = ports.FirstOrDefault(p => string.Equals(p.PortName, wanted, StringComparison.OrdinalIgnoreCase))
                                ?? ports.FirstOrDefault(p => p.IsBluetooth)
                                ?? (string.IsNullOrWhiteSpace(wanted) ? ports.FirstOrDefault() : null);
        if (match is not null)
        {
            SerialPortBox.SelectedItem = match;
        }
        else
        {
            SerialPortBox.Text = wanted ?? string.Empty;
        }
    }

    /// <summary>The chosen port's name: from the list, or "COM7" typed in by hand.</summary>
    private string? SelectedPortName()
    {
        if (SerialPortBox.SelectedItem is SerialPortInfo info)
        {
            return info.PortName;
        }

        string typed = SerialPortBox.Text.Trim();
        string name = new(typed.TakeWhile(c => !char.IsWhiteSpace(c) && c != '—').ToArray());
        return name.Length == 0 ? null : name.ToUpperInvariant();
    }

    private void OnRefreshPorts(object sender, RoutedEventArgs e)
    {
        LoadPorts(SelectedPortName());
        if (SerialPortBox.Items.Count == 0)
        {
            MessageBox.Show(this, "No COM ports were found.\n\nFor a Bluetooth printer: switch it on, then pair it in Windows Settings › Bluetooth & devices. " +
                "Windows gives a paired Bluetooth printer a COM port; press ↻ again afterwards.", "Thermal print", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void OnTestSerial(object sender, RoutedEventArgs e)
    {
        string? port = SelectedPortName();
        if (port is null)
        {
            MessageBox.Show(this, "Choose the printer's COM port first.", "Thermal print", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var button = (Button)sender;
        button.IsEnabled = false;
        Cursor = System.Windows.Input.Cursors.Wait;
        try
        {
            await SerialSink.TestAsync(port, BaudBox.SelectedItem is int baud ? baud : 9600);
            MessageBox.Show(this, $"{port} opened. The printer is ready to print.", "Thermal print", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Thermal print", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            button.IsEnabled = true;
            Cursor = null;
        }
    }

    // ---- network (Wi-Fi / cable) -------------------------------------------------------

    /// <summary>Stops the network search that is running, if any (the Stop button, or closing the window).</summary>
    private Action? _stopFinding;

    /// <summary>Looks for printers answering on port 9100 on this PC's networks; pressing again stops.</summary>
    private async void OnFindNetwork(object sender, RoutedEventArgs e)
    {
        if (_stopFinding is not null)
        {
            _stopFinding();
            return;
        }

        FoundRow.Visibility = Visibility.Visible;
        FoundList.Items.Clear();
        FindButton.Content = "Stop";
        using var cts = new CancellationTokenSource();
        _stopFinding = cts.Cancel;
        try
        {
            var progress = new Progress<int>(p => FoundText.Text = $"Looking for printers on your network… {p}%");
            IReadOnlyList<FoundPrinter> found = await NetworkPrinterFinder.FindAsync(progress, cts.Token);
            foreach (FoundPrinter printer in found)
            {
                FoundList.Items.Add(printer);
            }

            FoundText.Text = found.Count == 0
                ? "No printer answered on port 9100. Check the printer is on the same Wi-Fi or network as this PC, or type its address (it is often printed on the printer's settings label)."
                : $"Found {found.Count} printer(s). Click one to use it.";
            FoundList.Visibility = found.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            FoundText.Text = "Stopped.";
        }
        finally
        {
            _stopFinding = null;
            FindButton.Content = "Find printers…";
        }
    }

    private void OnFoundSelected(object sender, SelectionChangedEventArgs e)
    {
        if (FoundList.SelectedItem is FoundPrinter printer)
        {
            AddressBox.Text = printer.AddressText;
        }
    }

    private async void OnTestNetwork(object sender, RoutedEventArgs e)
    {
        try
        {
            (string host, int port) = ThermalSinks.ParseAddress(AddressBox.Text);
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            await client.ConnectAsync(host, port, timeout.Token);
            MessageBox.Show(this, $"The printer at {host}:{port} answered.", "Thermal print", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or InvalidOperationException)
        {
            string reason = ex is OperationCanceledException ? "no answer within 4 seconds" : ex.Message;
            MessageBox.Show(this, $"Could not reach the printer: {reason}", "Thermal print", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnPrint(object sender, RoutedEventArgs e)
    {
        Apply();
        if (_planStale || _plan is null)
        {
            Refresh();
        }

        if (_plan is null || _plan.Count == 0)
        {
            return;
        }

        ThermalPrintSettings settings = Thermal.Clone();
        string? file = null;
        if (settings.Destination == ThermalDestination.File)
        {
            var dialog = new SaveFileDialog
            {
                Filter = Localization.Localizer.Filter(settings.Language == ThermalLanguage.Zpl ? "ZPL commands|*.zpl|All files|*.*" : "TSPL commands|*.prn|All files|*.*"),
                FileName = SafeName(_session.Project.Metadata.Title) + ThermalEncoders.For(settings.Language).FileExtension,
                Title = Localization.Localizer.T("Save printer commands"),
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            file = dialog.FileName;
        }

        ThermalJobPlan plan = _plan;
        int copies = Math.Max(1, Print.Copies);
        IThermalSink sink;
        try
        {
            sink = ThermalSinks.Open(settings, file, $"LabelAxis — {_session.Project.Metadata.Title}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Thermal print", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        PrintLog log = PrintLog.Start(_history, _session, Print, plan.Summary, PrintHistoryKind.Printer, $"{settings.Language.ToString().ToUpperInvariant()} → {sink.Description}", file);
        CountingProgress? counter = null;
        try
        {
            using (sink)
            {
                string heading = $"Printing {plan.Count:N0} label row{(plan.Count == 1 ? "" : "s")} ({settings.Language.ToString().ToUpperInvariant()})";
                JobProgressWindow.Run(this, heading, "sent to " + sink.Description, plan.Count,
                    (progress, token) => plan.SendAsync(settings, sink, copies, counter = new CountingProgress(progress), token));
            }

            log.Completed();
            Print.FirstSheet = null;
            Print.LastSheet = null;
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            log.Cancelled(counter?.Max ?? 0);
            SummaryText.Text = $"Stopped after {counter?.Max ?? 0:N0} row(s). Print history can finish the rest.";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or SocketException or UnauthorizedAccessException)
        {
            log.Failed(counter?.Max ?? 0, ex);
            MessageBox.Show(this, "Printing stopped:\n\n" + ex.Message, "Thermal print", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string SafeName(string title)
    {
        string name = string.Concat(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        return name.Length == 0 ? "labels" : name;
    }

    private static int? ParseRow(string s) =>
        int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v > 0 ? v : null;

    private static double ParseDouble(string s, double fallback) =>
        double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
}
