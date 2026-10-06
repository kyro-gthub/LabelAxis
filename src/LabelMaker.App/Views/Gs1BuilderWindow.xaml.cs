using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LabelMaker.App.Localization;
using LabelMaker.App.Rendering;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Barcodes.Gs1;
using LabelMaker.Core.Serialization;

namespace LabelMaker.App.Views;

/// <summary>
/// Builds GS1 barcode data the right way: pick what the label is for (food with expiry and
/// batch, a medical device UDI, a pallet SSCC…), fill in each piece of data, and every problem
/// is shown in plain words as you type — wrong lengths, check digits, dates that do not exist,
/// data that needs other data with it. The result is a GS1-128, GS1 DataMatrix or GS1 QR code.
/// </summary>
public partial class Gs1BuilderWindow : Window
{
    /// <summary>A starting point: the AIs a kind of label usually carries, and its usual barcode.</summary>
    private sealed record Preset(string Name, string Note, string[] Ais, BarcodeSymbology Kind)
    {
        public override string ToString() => Localizer.T(Name);
    }

    private static readonly Preset[] Presets =
    {
        new("Your own choice of data", "Add the data you need with + Add data.", Array.Empty<string>(), BarcodeSymbology.GS1_128),
        new("Food: expiry date and batch", "Product number, use-by date and batch, on a carton or a pack of fresh food.",
            new[] { "01", "17", "10" }, BarcodeSymbology.GS1_128),
        new("Food: best before, batch and weight", "For packs of different weights: the weight in kg with its decimals.",
            new[] { "01", "15", "10", "310n" }, BarcodeSymbology.GS1_128),
        new("Fresh food sold by weight, with price", "Weight and price on a pack weighed in the shop.",
            new[] { "01", "310n", "392n" }, BarcodeSymbology.GS1_128),
        new("Medical device (UDI)", "Device identifier (01) and production identifiers: manufacture date, expiry, lot and serial. Keep only the ones your device uses.",
            new[] { "01", "11", "17", "10", "21" }, BarcodeSymbology.DataMatrix),
        new("Medicine pack (serialised)", "Product number, expiry, batch and a serial number for each pack.",
            new[] { "01", "17", "10", "21" }, BarcodeSymbology.DataMatrix),
        new("Carton of products", "A carton without its own product number: the products inside and how many.",
            new[] { "02", "37", "10", "15" }, BarcodeSymbology.GS1_128),
        new("Pallet or parcel (SSCC)", "The 18-digit shipping container code.", new[] { "00" }, BarcodeSymbology.GS1_128),
        new("Parcel with the delivery post code", "SSCC and the post code it goes to.", new[] { "00", "420" }, BarcodeSymbology.GS1_128),
    };

    /// <summary>An AI in the list, written in the interface language.</summary>
    private sealed record AiOption(Gs1Ai Ai, string Display)
    {
        public override string ToString() => Display;
    }

    /// <summary>One piece of data: its AI, decimals for the weight and price AIs, and the value.</summary>
    private sealed class Row
    {
        public required ComboBox AiBox { get; init; }
        public required ComboBox DecimalsBox { get; init; }
        public required TextBox ValueBox { get; init; }
        public required Button FixButton { get; init; }
        public required TextBlock NoteText { get; init; }
        public required FrameworkElement Root { get; init; }

        /// <summary>An AI the list does not have (from an existing barcode), kept as it was.</summary>
        public string? OtherAi { get; set; }

        public Gs1Ai? Ai => (AiBox.SelectedItem as AiOption)?.Ai;

        public string Code => Ai is { } ai ? ai.Written(DecimalsBox.SelectedIndex < 0 ? 0 : DecimalsBox.SelectedIndex) : OtherAi ?? string.Empty;
    }

    private readonly List<AiOption> _options;
    private readonly List<Row> _rows = new();
    private readonly WpfImageExporter _exporter = new(new SheetVisualRenderer(new WpfTextMeasurer(), new WpfImageSource(new AssetStore())));
    private bool _loading;

    /// <param name="editing">The barcode to change, or null to make a new one.</param>
    public Gs1BuilderWindow(BarcodeSpec? editing = null)
    {
        InitializeComponent();
        _options = Gs1.All.Select(a => new AiOption(a, $"({a.Code}) {Localizer.T(a.Title)}")).ToList();
        PresetBox.ItemsSource = Presets;

        _loading = true;
        if (editing is not null && Gs1.Parse(editing.Data) is { Count: > 0 } elements)
        {
            PresetBox.SelectedIndex = 0;
            SetKind(editing.Symbology);
            foreach (Gs1Element e in elements)
            {
                AddRow(e.Ai, e.Value);
            }

            UseButton.Content = Localizer.T("Change the barcode");
        }
        else
        {
            PresetBox.SelectedIndex = 1;
            ApplyPreset(Presets[1]);
        }

        _loading = false;
        Loaded += (_, _) => Refresh();
    }

    /// <summary>The barcode made, when the window closes with "Put on the label".</summary>
    public BarcodeSpec? Result { get; private set; }

    /// <summary>For a square code: the data as lines of text to put beside it, or null.</summary>
    public string? TextBeside { get; private set; }

    private BarcodeSymbology Kind => DataMatrixBox.IsChecked == true ? BarcodeSymbology.DataMatrix
        : QrBox.IsChecked == true ? BarcodeSymbology.QrCode
        : BarcodeSymbology.GS1_128;

    private void SetKind(BarcodeSymbology kind)
    {
        DataMatrixBox.IsChecked = kind == BarcodeSymbology.DataMatrix;
        QrBox.IsChecked = kind == BarcodeSymbology.QrCode;
        Gs1128Box.IsChecked = kind is not (BarcodeSymbology.DataMatrix or BarcodeSymbology.QrCode);
    }

    // ---- presets and rows ------------------------------------------------------------------

    private void OnPreset(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && PresetBox.SelectedItem is Preset preset)
        {
            ApplyPreset(preset);
            Refresh();
        }
    }

    private void ApplyPreset(Preset preset)
    {
        PresetNote.Text = Localizer.T(preset.Note);
        if (preset.Ais.Length == 0)
        {
            return; // "your own choice" keeps what is there
        }

        // Values already typed are kept for the AIs the new choice also has.
        Dictionary<string, string> typed = _rows.Where(r => r.Ai is not null)
            .GroupBy(r => r.Ai!.Code).ToDictionary(g => g.Key, g => g.First().ValueBox.Text);
        foreach (Row row in _rows.ToList())
        {
            RemoveRow(row);
        }

        foreach (string ai in preset.Ais)
        {
            AddRow(ai.Replace("n", "3", StringComparison.Ordinal), typed.GetValueOrDefault(ai, string.Empty));
        }

        SetKind(preset.Kind);
    }

    private void OnAddRow(object sender, RoutedEventArgs e)
    {
        Row row = AddRow("10", string.Empty);
        PresetBox.SelectedIndex = 0;
        row.ValueBox.Focus();
        Refresh();
    }

    private Row AddRow(string code, string value)
    {
        var aiBox = new ComboBox { ItemsSource = _options, Width = 270, Margin = new Thickness(0, 0, 8, 0) };
        Localizer.SetOff(aiBox, true);
        var decimals = new ComboBox { Width = 64, Margin = new Thickness(0, 0, 8, 0), ToolTip = Localizer.T("Digits after the decimal point") };
        for (int i = 0; i <= 5; i++)
        {
            decimals.Items.Add(i.ToString(CultureInfo.CurrentCulture));
        }

        var valueBox = new TextBox { Text = value, FontFamily = new FontFamily("Consolas"), VerticalContentAlignment = VerticalAlignment.Center };
        Localizer.SetOff(valueBox, true);
        var fix = new Button { Visibility = Visibility.Collapsed, Margin = new Thickness(8, 3, 0, 0), Padding = new Thickness(10, 2, 10, 2), VerticalAlignment = VerticalAlignment.Top };
        fix.SetResourceReference(StyleProperty, "GhostButton");
        var remove = new Button { Content = "✕", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 3, 8, 3), ToolTip = Localizer.T("Remove") };
        remove.SetResourceReference(StyleProperty, "IconGhost");
        var note = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };

        var line = new DockPanel();
        DockPanel.SetDock(aiBox, Dock.Left);
        DockPanel.SetDock(decimals, Dock.Left);
        DockPanel.SetDock(remove, Dock.Right);

        line.Children.Add(aiBox);
        line.Children.Add(decimals);
        line.Children.Add(remove);

        line.Children.Add(valueBox);
        // Under the line: the hint or the problem, with its quick fix beside it.
        var under = new DockPanel();
        DockPanel.SetDock(fix, Dock.Right);
        under.Children.Add(fix);
        under.Children.Add(note);
        var root = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        root.Children.Add(line);
        root.Children.Add(under);

        var row = new Row { AiBox = aiBox, DecimalsBox = decimals, ValueBox = valueBox, FixButton = fix, NoteText = note, Root = root };
        Gs1Ai? known = Gs1.Find(code);
        if (known is not null)
        {
            aiBox.SelectedItem = _options.First(o => o.Ai == known);
            decimals.SelectedIndex = known.HasDecimalDigit ? code[^1] - '0' : 0;
        }
        else
        {
            row.OtherAi = code;
            var other = new AiOption(new Gs1Ai(code, Localizer.T("Other"), Gs1Charset.Alphanumeric, 1, 90), $"({code}) {Localizer.T("Other")}");
            aiBox.ItemsSource = _options.Append(other).ToList();
            aiBox.SelectedItem = other;
        }

        aiBox.SelectionChanged += (_, _) => Refresh();
        decimals.SelectionChanged += (_, _) => Refresh();
        valueBox.TextChanged += (_, _) => Refresh();
        valueBox.LostFocus += (_, _) =>
        {
            string tidy = Gs1.Tidy(row.Code, valueBox.Text);
            if (tidy != valueBox.Text)
            {
                valueBox.Text = tidy;
            }
        };
        remove.Click += (_, _) =>
        {
            RemoveRow(row);
            PresetBox.SelectedIndex = 0;
            Refresh();
        };
        fix.Click += (_, _) => Fix(row);

        _rows.Add(row);
        RowList.Children.Add(root);
        return row;
    }

    private void RemoveRow(Row row)
    {
        _rows.Remove(row);
        RowList.Children.Remove(row.Root);
    }

    /// <summary>The quick fix a row offers: the missing or wrong check digit, or 14 digits for (01).</summary>
    private static (string Label, string Value)? FixFor(Row row)
    {
        string v = row.ValueBox.Text.Trim();
        if (row.Ai is not { Kind: Gs1ValueKind.CheckDigit } ai || Gs1.IsFromData(v) || v.Length == 0 || !v.All(char.IsAsciiDigit))
        {
            return null;
        }

        if (v.Length == ai.MaxLength - 1)
        {
            return ("Add check digit", v + Gs1.CheckDigit(v));
        }

        if (v.Length == ai.MaxLength && !Gs1.HasValidCheckDigit(v))
        {
            return ("Correct check digit", v[..^1] + Gs1.CheckDigit(v[..^1]));
        }

        if (ai.Code is "01" or "02" && v.Length < 14 && Gs1.AsGtin14(v) is { } gtin && Gs1.HasValidCheckDigit(gtin))
        {
            return ("Make it 14 digits", gtin);
        }

        return null;
    }

    private void Fix(Row row)
    {
        if (FixFor(row) is { } fix)
        {
            row.ValueBox.Text = fix.Value;
        }
    }

    // ---- checking and the preview -----------------------------------------------------------

    private void OnChanged(object sender, RoutedEventArgs e) => Refresh();

    private List<Gs1Element> Elements()
    {
        var elements = _rows.Select(r => new Gs1Element(r.Code, r.ValueBox.Text.Trim())).ToList();
        return OrderBox.IsChecked == true ? Gs1.RecommendedOrder(elements) : elements;
    }

    private void Refresh()
    {
        if (_loading || !IsLoaded)
        {
            return;
        }

        bool square = Kind != BarcodeSymbology.GS1_128;
        TextBox2d.Visibility = square ? Visibility.Visible : Visibility.Collapsed;

        // Problems are worked out on the rows as shown, so each lands under its own row.
        var shown = _rows.Select(r => new Gs1Element(r.Code, r.ValueBox.Text.Trim())).ToList();
        List<Gs1Problem> problems = Gs1.Check(shown);
        for (int i = 0; i < _rows.Count; i++)
        {
            Row row = _rows[i];
            // An empty box is not yet a problem: it says what to type instead.
            Gs1Problem? mine = row.ValueBox.Text.Trim().Length == 0
                ? null
                : problems.Where(p => p.Index == i).OrderByDescending(p => p.IsError).FirstOrDefault();
            string hint = row.Ai is not { } ai ? string.Empty
                : ai.Hint.Length > 0 ? ai.Hint
                : ai.Charset == Gs1Charset.Numeric ? Localizer.F("Up to {0} digits", ai.MaxLength)
                : Localizer.F("Up to {0} letters or digits", ai.MaxLength);
            row.DecimalsBox.Visibility = row.Ai?.HasDecimalDigit == true ? Visibility.Visible : Visibility.Collapsed;
            if (mine is not null)
            {
                row.NoteText.Text = Localizer.T(mine.Message);
                row.NoteText.SetResourceReference(TextBlock.ForegroundProperty, mine.IsError ? "Br.Danger" : "Br.Accent");
            }
            else
            {
                row.NoteText.Text = Gs1.IsFromData(row.ValueBox.Text) ? Localizer.T("Filled in from your data when printing.")
                    : row.Ai?.Kind == Gs1ValueKind.Date ? Localizer.T("YYMMDD, or a date such as 31/12/2026")
                    : Localizer.T(hint);
                row.NoteText.SetResourceReference(TextBlock.ForegroundProperty, "Br.TextMuted");
            }

            if (FixFor(row) is { } fix)
            {
                row.FixButton.Content = Localizer.T(fix.Label);
                row.FixButton.Visibility = Visibility.Visible;
            }
            else
            {
                row.FixButton.Visibility = Visibility.Collapsed;
            }
        }

        List<Gs1Element> elements = Elements();
        ReadableText.Text = square
            ? string.Join("\n", elements.Select(e => $"({e.Ai}) {e.Value}"))
            : Gs1.Format(elements);

        ProblemList.Children.Clear();
        bool errors = problems.Any(p => p.IsError);
        var empty = shown.Where(e => e.Value.Length == 0).Select(e => $"({e.Ai})").ToList();
        if (!errors)
        {
            AddStatus("✓  " + Localizer.T("Correct GS1 data: ready to put on the label."), "Br.Ok");
        }
        else if (empty.Count > 0)
        {
            AddStatus(Localizer.F("Fill in {0}.", string.Join(", ", empty)), "Br.Text2");
        }

        foreach (Gs1Problem p in problems.Where(p => p.Index < 0 || p.Index >= shown.Count || shown[p.Index].Value.Length > 0).OrderByDescending(p => p.IsError))
        {
            AddStatus((p.IsError ? "✕  " : "!  ") + Localizer.T(p.Message), p.IsError ? "Br.Danger" : "Br.Accent");
        }

        UseButton.IsEnabled = !errors;
        DrawPreview(elements);
    }

    private void AddStatus(string text, string brush)
    {
        var line = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 0, 0, 6) };
        line.SetResourceReference(TextBlock.ForegroundProperty, brush);
        Localizer.SetOff(line, true);
        ProblemList.Children.Add(line);
    }

    /// <summary>The barcode as it will look; data fields are drawn with sample values.</summary>
    private void DrawPreview(List<Gs1Element> elements)
    {
        PreviewImage.Source = null;
        if (elements.Count == 0)
        {
            return;
        }

        var samples = elements.Select(e => Gs1.IsFromData(e.Value) || e.Value.Length == 0 ? e with { Value = Sample(e.Ai) } : e).ToList();
        BarcodeSpec spec = MakeSpec(Gs1.Format(samples));
        try
        {
            byte[] png = BarcodeRasterizer.RenderPng(_exporter, spec, spec.Data, dpi: 200);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(png);
            image.EndInit();
            image.Freeze();
            PreviewImage.Source = image;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
        {
            // The status list already says what is wrong.
        }
    }

    // A value that passes the AI's checks, standing in for a data field in the preview.
    private static string Sample(string code)
    {
        Gs1Ai? ai = Gs1.Find(code);
        return ai?.Kind switch
        {
            Gs1ValueKind.Date => Gs1.Date(DateTime.Today.AddYears(1)),
            Gs1ValueKind.DateTime => Gs1.Date(DateTime.Today.AddYears(1)) + "1200",
            Gs1ValueKind.CheckDigit => new string('0', ai.MaxLength - 2) + "1" + Gs1.CheckDigit(new string('0', ai.MaxLength - 2) + "1"),
            _ when ai?.Charset == Gs1Charset.Numeric => new string('1', Math.Max(1, ai.MinLength)),
            _ => "ABC123",
        };
    }

    private BarcodeSpec MakeSpec(string data)
    {
        BarcodeSymbology kind = Kind;
        return kind switch
        {
            BarcodeSymbology.DataMatrix => new BarcodeSpec
            {
                Symbology = BarcodeSymbology.DataMatrix, Gs1Mode = true, Data = data, ModuleWidthMm = 0.5, HumanReadable = HumanReadablePosition.None,
            },
            BarcodeSymbology.QrCode => new BarcodeSpec
            {
                Symbology = BarcodeSymbology.QrCode, Gs1Mode = true, Data = data, ModuleWidthMm = 0.5, HumanReadable = HumanReadablePosition.None,
                QrEcLevel = QrErrorCorrection.Medium,
            },
            _ => new BarcodeSpec
            {
                Symbology = BarcodeSymbology.GS1_128, Gs1Mode = true, Data = data, ModuleWidthMm = 0.33, BarHeightMm = 15,
                HumanReadable = HumanReadablePosition.Below,
            },
        };
    }

    private void OnUse(object sender, RoutedEventArgs e)
    {
        List<Gs1Element> elements = Elements();
        if (Gs1.Check(elements).Any(p => p.IsError))
        {
            return;
        }

        Result = MakeSpec(Gs1.Format(elements));
        TextBeside = Kind != BarcodeSymbology.GS1_128 && TextBox2d.IsChecked == true
            ? string.Join("\n", elements.Select(el => $"({el.Ai}) {el.Value}"))
            : null;
        DialogResult = true;
    }
}
