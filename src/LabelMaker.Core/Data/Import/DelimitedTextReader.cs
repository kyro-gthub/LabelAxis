using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace LabelMaker.Core.Data.Import;

/// <summary>Reads CSV / TSV / TXT via CsvHelper, with delimiter and encoding auto-detection.</summary>
public sealed class DelimitedTextReader : ITabularReader
{
    private static readonly char[] CandidateDelimiters = { ',', ';', '\t', '|' };

    public RawTable Read(Stream stream, TabularReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            stream.CopyTo(ms);
            bytes = ms.ToArray();
        }

        Encoding encoding = ResolveEncoding(options.EncodingName, bytes);
        string text = encoding.GetString(bytes);
        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        string delimiter = options.Delimiter ?? DetectDelimiter(text);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            HasHeaderRecord = false,
            DetectColumnCountChanges = false,
            MissingFieldFound = null,
            BadDataFound = null,
            TrimOptions = TrimOptions.None,
            IgnoreBlankLines = false,
        };

        var rows = new List<IReadOnlyList<string>>();
        using var reader = new StringReader(text);
        using var csv = new CsvReader(reader, config);
        while (csv.Read())
        {
            int count = csv.Parser.Count;
            var row = new string[count];
            for (int i = 0; i < count; i++)
            {
                row[i] = csv.GetField(i) ?? string.Empty;
            }

            rows.Add(row);
            if (options.MaxRows > 0 && rows.Count >= options.MaxRows)
            {
                break;
            }
        }

        // Drop a trailing fully-empty row (common with a final newline).
        if (rows.Count > 0 && rows[^1].All(string.IsNullOrEmpty))
        {
            rows.RemoveAt(rows.Count - 1);
        }

        return new RawTable { Rows = rows };
    }

    private static Encoding ResolveEncoding(string? name, byte[] bytes)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            try
            {
                return Encoding.GetEncoding(name);
            }
            catch (ArgumentException)
            {
                // fall through to detection
            }
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode;
        }

        // Heuristic: valid UTF-8?  Otherwise Latin-1 so no bytes are lost.
        try
        {
            _ = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            return new UTF8Encoding(false);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1;
        }
    }

    private static string DetectDelimiter(string sample)
    {
        string firstLines = string.Join('\n',
            sample.Replace("\r\n", "\n").Split('\n').Take(20));

        char best = ',';
        int bestScore = -1;
        foreach (char d in CandidateDelimiters)
        {
            var counts = firstLines.Split('\n')
                .Where(l => l.Length > 0)
                .Select(l => l.Count(ch => ch == d))
                .ToList();
            if (counts.Count == 0 || counts.Sum() == 0)
            {
                continue;
            }

            // Prefer a delimiter that appears consistently on every line.
            int min = counts.Min();
            int score = min > 0 ? (min * 100) - (counts.Max() - min) : counts.Sum();
            if (score > bestScore)
            {
                bestScore = score;
                best = d;
            }
        }

        return best.ToString();
    }
}
