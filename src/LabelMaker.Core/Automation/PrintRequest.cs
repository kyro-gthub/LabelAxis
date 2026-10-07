using System.Text.Json;
using System.Text.Json.Serialization;
using LabelMaker.Core.Data;

namespace LabelMaker.Core.Automation;

/// <summary>What a print request asks for: printed labels, or a PDF of them.</summary>
public enum PrintRequestOutput
{
    Print = 0,
    Pdf = 1,
}

/// <summary>
/// A request from another program to print labels, by the watch folder (a .json or .csv
/// file dropped in) or the local web link (POST /print). Which labels: one of the user's
/// designs by name, or a project file. What goes on them: one label per record, the record's
/// values filling the design's {Field}s. Example:
/// <code>
/// { "design": "Price tag", "printer": "Zebra ZD421", "copies": 2,
///   "records": [ { "Product": "Milk 1 L", "Price": "1.25", "Barcode": "5012345678900" } ] }
/// </code>
/// </summary>
public sealed class PrintRequest
{
    /// <summary>The name of one of the user's designs (File › Save page as my design).</summary>
    public string? Design { get; set; }

    /// <summary>The full path of a project file (.labelproj), instead of a design.</summary>
    public string? Project { get; set; }

    /// <summary>A Windows printer's name, or "thermal" for the design's thermal printer settings. Empty: as last used.</summary>
    public string? Printer { get; set; }

    /// <summary>How many of each label.</summary>
    public int Copies { get; set; } = 1;

    /// <summary>One label per record; field names as in the design. None: the design as it is.</summary>
    public List<Dictionary<string, string>> Records { get; set; } = new();

    /// <summary>Print, or make a PDF (returned by the web link, or saved beside the request file).</summary>
    public PrintRequestOutput Output { get; set; }

    /// <summary>For a sheet of labels: the label position to start at (1 = top left), to use a part-used sheet.</summary>
    public int? StartPosition { get; set; }

    /// <summary>The caller's own reference, given back in the reply and kept in the log.</summary>
    public string? Reference { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new JsonStringEnumConverter(), new LenientStringConverter() },
        MaxDepth = 16,
    };

    /// <summary>
    /// Reads a request. Throws <see cref="PrintRequestException"/> with a plain reason when the
    /// text is not a request (or is missing what to print, unless <paramref name="defaultDesign"/>
    /// is given).
    /// </summary>
    public static PrintRequest FromJson(string json, string? defaultDesign = null)
    {
        PrintRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<PrintRequest>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new PrintRequestException("The request is not valid JSON: " + ex.Message, ex);
        }

        return (request ?? throw new PrintRequestException("The request is empty.")).Checked(defaultDesign);
    }

    /// <summary>
    /// A request from a CSV or tab-separated text: the first row names the fields, each other
    /// row is a label. Printed with <paramref name="design"/> (the default design when none).
    /// </summary>
    public static PrintRequest FromDelimited(string text, string? design, string? printer = null, int copies = 1)
    {
        ArgumentNullException.ThrowIfNull(text);
        Data.Import.RawTable raw;
        try
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
            raw = new Data.Import.DelimitedTextReader().Read(stream, new Data.Import.TabularReadOptions());
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or ArgumentException or CsvHelper.CsvHelperException)
        {
            throw new PrintRequestException("The CSV could not be read: " + ex.Message, ex);
        }

        if (raw.RowCount == 0)
        {
            throw new PrintRequestException("The CSV is empty: its first row should name the fields, each other row is a label.");
        }

        IReadOnlyList<string> headers = raw.Rows[0].Select(h => h.Trim()).ToList();
        var request = new PrintRequest { Design = design, Printer = printer, Copies = copies };
        foreach (IReadOnlyList<string> row in raw.Rows.Skip(1).Where(r => r.Any(v => !string.IsNullOrWhiteSpace(v))))
        {
            var record = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Count; i++)
            {
                if (headers[i].Length > 0)
                {
                    record[headers[i]] = i < row.Count ? row[i] : string.Empty;
                }
            }

            request.Records.Add(record);
        }

        return request.Checked(design);
    }

    private PrintRequest Checked(string? defaultDesign)
    {
        Records ??= new List<Dictionary<string, string>>(); // "records": null is the same as none
        if (string.IsNullOrWhiteSpace(Design) && string.IsNullOrWhiteSpace(Project))
        {
            Design = defaultDesign;
        }

        if (string.IsNullOrWhiteSpace(Design) && string.IsNullOrWhiteSpace(Project))
        {
            throw new PrintRequestException("Say which labels to print: \"design\" (the name of one of your designs) or \"project\" (a project file).");
        }

        if (Copies is < 1 or > 10_000)
        {
            throw new PrintRequestException("\"copies\" must be from 1 to 10000.");
        }

        if (Records.Count * (long)Copies > 100_000)
        {
            throw new PrintRequestException("A request can print at most 100000 labels.");
        }

        if (StartPosition is < 1)
        {
            throw new PrintRequestException("\"startPosition\" starts at 1, the top-left label.");
        }

        // Field names match in any case; if a record names one twice ("Price" and "price"), the last wins.
        Records = Records.Where(r => r is not null).Select(r =>
        {
            var record = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach ((string key, string value) in r)
            {
                record[key] = value ?? string.Empty;
            }

            return record;
        }).ToList();
        return this;
    }

    /// <summary>
    /// The records as a data set, each repeated <see cref="Copies"/> times one after the other;
    /// with no records, that many labels of the design as it is.
    /// </summary>
    public DataSet ToDataSet()
    {
        List<string> names = Records.SelectMany(r => r.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var columns = names.Select((n, i) => new DataColumn(n, i)).ToList();
        var data = new DataSet(columns, new List<DataRecord>());
        IEnumerable<Dictionary<string, string>> rows = Records.Count > 0 ? Records : new[] { new Dictionary<string, string>() };
        foreach (Dictionary<string, string> record in rows)
        {
            string[] values = names.Select(n => record.TryGetValue(n, out string? v) ? v ?? string.Empty : string.Empty).ToArray();
            for (int copy = 0; copy < Copies; copy++)
            {
                data.InsertRow(data.Rows.Count, data.CreateRecord(values));
            }
        }

        return data;
    }

    /// <summary>A line for the log: what was asked for.</summary>
    public string Describe()
    {
        string what = !string.IsNullOrWhiteSpace(Design) ? $"“{Design}”" : Path.GetFileName(Project ?? string.Empty);
        int labels = Math.Max(1, Records.Count) * Copies;
        return $"{labels} × {what}" + (Output == PrintRequestOutput.Pdf ? " → PDF" : string.Empty) +
               (string.IsNullOrWhiteSpace(Reference) ? string.Empty : $" ({Reference})");
    }

    /// <summary>Lets numbers and true/false stand for text in records ("price": 1.25).</summary>
    private sealed class LenientStringConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => System.Text.Encoding.UTF8.GetString(reader.ValueSpan),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => string.Empty,
            _ => throw new JsonException("A value must be text or a number."),
        };

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
    }
}

/// <summary>A print request that cannot be carried out, with the reason in plain words.</summary>
public sealed class PrintRequestException : Exception
{
    public PrintRequestException(string message)
        : base(message)
    {
    }

    public PrintRequestException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>The answer to a print request.</summary>
public sealed record PrintRequestResult(bool Ok, string Message, int Labels = 0, string? Reference = null)
{
    /// <summary>The PDF made, when the request asked for one.</summary>
    [JsonIgnore]
    public byte[]? Pdf { get; init; }
}
