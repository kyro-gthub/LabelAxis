using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using LabelMaker.App.Automation;
using LabelMaker.App.Localization;
using LabelMaker.App.Services;
using LabelMaker.Core.Settings;
using Microsoft.Win32;

namespace LabelMaker.App.Views;

/// <summary>
/// Switches the watch folder and the web link on and off, says what is running, shows the
/// requests that came in, and gives an example request to copy.
/// </summary>
public partial class AutomationWindow : Window
{
    private readonly AppEnvironment _env;
    private readonly AutomationHost _host;
    private string _key;

    public AutomationWindow(AppEnvironment env, AutomationHost host)
    {
        InitializeComponent();
        _env = env;
        _host = host;
        AutomationSettings s = env.Settings.Automation;
        _key = s.AccessKey ?? AutomationSettings.NewAccessKey();

        FolderOnBox.IsChecked = s.WatchFolderEnabled;
        FolderBox.Text = s.WatchFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LabelAxis print requests");
        WebOnBox.IsChecked = s.WebLinkEnabled;
        PortBox.Text = s.WebLinkPort.ToString(CultureInfo.InvariantCulture);
        NetworkBox.IsChecked = s.AllowOtherComputers;
        KeyBox.Text = _key;
        OnNetworkChanged(this, new RoutedEventArgs());

        var designs = new List<string> { Localizer.T("(none)") };
        designs.AddRange(host.Runner.DesignNames());
        DesignBox.ItemsSource = designs;
        DesignBox.SelectedItem = s.DefaultDesign is { } d && designs.Contains(d) ? d : designs[0];

        var printers = new List<string> { Localizer.T("As the design says") };
        printers.AddRange(host.Runner.PrinterNames());
        printers.Add("thermal");
        PrinterBox.ItemsSource = printers;
        PrinterBox.SelectedItem = s.DefaultPrinter is { } p && printers.Contains(p) ? p : printers[0];

        ExampleBox.Text = Example(designs.Count > 1 ? designs[1] : "Price tag");
        LogList.ItemsSource = host.Log;
        LogPathText.Text = Localizer.F("Also written to {0}", Path.Combine(env.Paths.LogDirectory, "print-requests.log"));
        host.Changed += OnHostChanged;
        Closed += (_, _) => host.Changed -= OnHostChanged;
        ShowStatus();
    }

    private void OnHostChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(ShowStatus);

    private void ShowStatus()
    {
        StatusText.Text = _host.Problem is { } problem ? _host.Status + " — " + problem : _host.Status;
        StatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,
            _host.Problem is not null ? "Br.Danger" : _host.Address is not null || _env.Settings.Automation.WatchFolderEnabled ? "Br.Ok" : "Br.TextMuted");
        EmptyLog.Visibility = _host.Log.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        OnPortChanged(PortBox, null!);
    }

    private static string Example(string design) =>
        "{\n" +
        $"  \"design\": \"{design.Replace("\"", "\\\"", StringComparison.Ordinal)}\",\n" +
        "  \"printer\": \"\",\n" +
        "  \"copies\": 1,\n" +
        "  \"records\": [\n" +
        "    { \"Product\": \"Milk 1 L\", \"Price\": \"1.25\", \"Barcode\": \"5012345678900\" },\n" +
        "    { \"Product\": \"Bread\", \"Price\": \"0.95\", \"Barcode\": \"5012345678917\" }\n" +
        "  ]\n" +
        "}\n";

    private int Port => int.TryParse(PortBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) && port is > 0 and < 65536
        ? port
        : AutomationSettings.DefaultPort;

    private void OnPortChanged(object sender, TextChangedEventArgs e) => AddressText.Text = $"http://localhost:{Port}/print";

    private void OnNetworkChanged(object sender, RoutedEventArgs e) => KeyRow.IsEnabled = NetworkBox.IsChecked == true;

    private void OnChooseFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Localizer.T("Folder for print requests"), InitialDirectory = Directory.Exists(FolderBox.Text) ? FolderBox.Text : null };
        if (dialog.ShowDialog(this) == true)
        {
            FolderBox.Text = dialog.FolderName;
        }
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(FolderBox.Text);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{FolderBox.Text}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, ex.Message, "LabelAxis", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnOpenLink(object sender, RoutedEventArgs e)
    {
        if (_host.Address is not { } address)
        {
            MessageBox.Show(this, Localizer.T("Switch the web link on and save first."), "LabelAxis", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo(address) { UseShellExecute = true });
    }

    private void OnNewKey(object sender, RoutedEventArgs e)
    {
        _key = AutomationSettings.NewAccessKey();
        KeyBox.Text = _key;
    }

    private void OnCopyKey(object sender, RoutedEventArgs e) => Clipboard.SetText(_key);

    private void OnCopyExample(object sender, RoutedEventArgs e) => Clipboard.SetText(ExampleBox.Text);

    private void OnSaveExample(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = Localizer.Filter("Print request|*.json"), FileName = "request.json" };
        if (dialog.ShowDialog(this) == true)
        {
            File.WriteAllText(dialog.FileName, ExampleBox.Text);
        }
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        AutomationSettings s = _env.Settings.Automation;
        s.WatchFolderEnabled = FolderOnBox.IsChecked == true;
        s.WatchFolder = string.IsNullOrWhiteSpace(FolderBox.Text) ? null : FolderBox.Text.Trim();
        s.WebLinkEnabled = WebOnBox.IsChecked == true;
        s.WebLinkPort = Port;
        s.AllowOtherComputers = NetworkBox.IsChecked == true;
        s.AccessKey = _key;
        s.DefaultDesign = DesignBox.SelectedIndex > 0 ? DesignBox.SelectedItem as string : null;
        s.DefaultPrinter = PrinterBox.SelectedIndex > 0 ? PrinterBox.SelectedItem as string : null;
        _env.SaveSettings();
        await _host.ApplyAsync();
        ShowStatus();
    }
}
