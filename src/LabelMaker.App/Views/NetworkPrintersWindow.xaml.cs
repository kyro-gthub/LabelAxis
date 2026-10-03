using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using LabelMaker.App.Printing;
using LabelMaker.Core.Printing.Ipp;
using LabelMaker.Core.Settings;

namespace LabelMaker.App.Views;

/// <summary>
/// Finds printers on the Wi-Fi or office network (or looks one up by address) and keeps a
/// list of them; the Print dialog then offers them next to the printers installed in Windows.
/// </summary>
public partial class NetworkPrintersWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Action _save;
    private Action? _stopSearch;

    public NetworkPrintersWindow(AppSettings settings, Action save)
    {
        _settings = settings;
        _save = save;
        InitializeComponent();
        ShowSaved();
        Closed += (_, _) => _stopSearch?.Invoke();
    }

    /// <summary>True when the list of saved printers changed.</summary>
    public bool Changed { get; private set; }

    private void ShowSaved()
    {
        SavedList.ItemsSource = _settings.NetworkPrinters.Select(p => new Row(p)).ToList();
        SavedEmpty.Visibility = _settings.NetworkPrinters.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RemoveButton.IsEnabled = false;
    }

    private async void OnFind(object sender, RoutedEventArgs e)
    {
        if (_stopSearch is not null)
        {
            _stopSearch();
            return;
        }

        using var cts = new CancellationTokenSource();
        _stopSearch = cts.Cancel;
        FindButton.Content = "Stop";
        FoundList.ItemsSource = null;
        try
        {
            var progress = new Progress<int>(p => FindText.Text = $"Looking… {p}%");
            IReadOnlyList<IppPrinterInfo> found = await WifiPrinterFinder.FindAsync(progress, cts.Token);
            ShowFound(found);
            FindText.Text = found.Count == 0
                ? "No printer answered. Check it is switched on and on the same Wi-Fi as this PC, or type its address."
                : $"Found {found.Count} printer(s). Pick one and press Add.";
        }
        catch (OperationCanceledException)
        {
            FindText.Text = "Stopped.";
        }
        finally
        {
            _stopSearch = null;
            FindButton.Content = "Find printers on my network";
        }
    }

    private async void OnLookUp(object sender, RoutedEventArgs e)
    {
        string text = AddressBox.Text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        // "192.168.1.40", "printer.local", "192.168.1.40:631" or a full ipp:// address.
        string host = text;
        int port = IppClient.Port;
        if (Uri.TryCreate(text.Replace("ipp://", "http://", StringComparison.OrdinalIgnoreCase), UriKind.Absolute, out Uri? uri) && text.Contains("://", StringComparison.Ordinal))
        {
            host = uri.Host;
            port = uri.IsDefaultPort ? IppClient.Port : uri.Port;
        }
        else if (text.LastIndexOf(':') is var colon and > 0 && int.TryParse(text[(colon + 1)..], out int p))
        {
            host = text[..colon];
            port = p;
        }

        LookUpButton.IsEnabled = false;
        FindText.Text = $"Asking {host}…";
        try
        {
            IppPrinterInfo? info = await IppClient.DescribeAsync(host, port, CancellationToken.None);
            if (info is null)
            {
                FindText.Text = $"{host} did not answer as a network printer (IPP, port {port}).";
                return;
            }

            ShowFound(new[] { info });
            FoundList.SelectedIndex = 0;
            FindText.Text = "Found it. Press Add to keep it.";
        }
        finally
        {
            LookUpButton.IsEnabled = true;
        }
    }

    private void ShowFound(IReadOnlyList<IppPrinterInfo> found)
    {
        FoundList.ItemsSource = found.Select(p => new Row(p)).ToList();
        AddButton.IsEnabled = false;
    }

    private void OnFoundSelected(object sender, SelectionChangedEventArgs e) =>
        AddButton.IsEnabled = FoundList.SelectedItem is Row { Printer.BestFormat: not IppDocumentFormat.None };

    private void OnSavedSelected(object sender, SelectionChangedEventArgs e) => RemoveButton.IsEnabled = SavedList.SelectedItem is Row;

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        if (FoundList.SelectedItem is not Row row)
        {
            return;
        }

        _settings.NetworkPrinters.RemoveAll(p => string.Equals(p.Uri, row.Printer.Uri, StringComparison.OrdinalIgnoreCase));
        _settings.NetworkPrinters.Add(row.Printer);
        _save();
        Changed = true;
        ShowSaved();
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (SavedList.SelectedItem is Row row)
        {
            _settings.NetworkPrinters.RemoveAll(p => string.Equals(p.Uri, row.Printer.Uri, StringComparison.OrdinalIgnoreCase));
            _save();
            Changed = true;
            ShowSaved();
        }
    }

    private void OnWindowsSettings(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:printers") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, "Windows printer settings could not be opened: " + ex.Message, "Wi-Fi printers", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnDone(object sender, RoutedEventArgs e) => DialogResult = Changed;

    private sealed record Row(IppPrinterInfo Printer)
    {
        public string Title => IppPrintJob.DisplayName(Printer);

        public string Detail => (Printer.Location.Length > 0 ? Printer.Location + " · " : string.Empty) + Printer.Describe();

        public override string ToString() => $"{Title}  —  {Printer.Describe()}";
    }
}
