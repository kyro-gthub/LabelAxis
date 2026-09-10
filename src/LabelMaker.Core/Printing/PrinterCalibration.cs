using System.Text.Json;
using System.Text.Json.Serialization;

namespace LabelMaker.Core.Printing;

/// <summary>
/// Per-printer physical output correction from the Calibration Wizard (spec §16).
/// Positive <see cref="OffsetXMm"/> moves the artwork right; positive
/// <see cref="OffsetYMm"/> moves it down. Scale factors are near 1.0 and correct
/// a driver that very slightly stretches the page.
/// </summary>
public sealed record PrinterCalibration
{
    public required string PrinterName { get; init; }

    public double OffsetXMm { get; init; }

    public double OffsetYMm { get; init; }

    public double ScaleX { get; init; } = 1.0;

    public double ScaleY { get; init; } = 1.0;

    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public string? Notes { get; init; }

    public static PrinterCalibration Identity(string printerName) => new() { PrinterName = printerName };

    public bool IsIdentity =>
        Math.Abs(OffsetXMm) < 1e-4 && Math.Abs(OffsetYMm) < 1e-4 &&
        Math.Abs(ScaleX - 1.0) < 1e-6 && Math.Abs(ScaleY - 1.0) < 1e-6;
}

/// <summary>
/// Persists <see cref="PrinterCalibration"/> per printer name (spec §16: stored
/// separately for each printer). Plain JSON dictionary; safe atomic writes.
/// </summary>
public sealed class CalibrationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Dictionary<string, PrinterCalibration> _byPrinter =
        new(StringComparer.OrdinalIgnoreCase);

    public string? FilePath { get; private set; }

    public IReadOnlyCollection<PrinterCalibration> All => _byPrinter.Values;

    public PrinterCalibration Get(string printerName) =>
        _byPrinter.TryGetValue(printerName, out PrinterCalibration? c)
            ? c
            : PrinterCalibration.Identity(printerName);

    public bool Has(string printerName) => _byPrinter.ContainsKey(printerName);

    public void Set(PrinterCalibration calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        _byPrinter[calibration.PrinterName] = calibration with { UpdatedUtc = DateTimeOffset.UtcNow };
    }

    public bool Remove(string printerName) => _byPrinter.Remove(printerName);

    public static CalibrationStore Load(string path)
    {
        var store = new CalibrationStore { FilePath = path };
        if (!File.Exists(path))
        {
            return store;
        }

        try
        {
            var list = JsonSerializer.Deserialize<List<PrinterCalibration>>(File.ReadAllText(path), JsonOptions)
                       ?? new List<PrinterCalibration>();
            foreach (PrinterCalibration c in list.Where(c => !string.IsNullOrWhiteSpace(c.PrinterName)))
            {
                store._byPrinter[c.PrinterName] = c;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Corrupt calibration file must never block printing — start clean.
        }

        return store;
    }

    public void Save(string? path = null)
    {
        path ??= FilePath ?? throw new InvalidOperationException("No calibration file path set.");
        FilePath = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        string tmp = path + ".tmp";
        string json = JsonSerializer.Serialize(_byPrinter.Values.OrderBy(c => c.PrinterName), JsonOptions);
        File.WriteAllText(tmp, json);
        if (File.Exists(path))
        {
            File.Replace(tmp, path, null);
        }
        else
        {
            File.Move(tmp, path);
        }
    }
}
