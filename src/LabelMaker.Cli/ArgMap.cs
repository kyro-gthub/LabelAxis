using System.Globalization;

namespace LabelMaker.Cli;

/// <summary>Minimal <c>--key value</c> / <c>--flag</c> parser. No external dependency.</summary>
internal sealed class ArgMap
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _positional = new();

    public static ArgMap Parse(IEnumerable<string> args)
    {
        var map = new ArgMap();
        string? pendingKey = null;

        foreach (string arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (pendingKey is not null)
                {
                    map._values[pendingKey] = null;
                }

                int eq = arg.IndexOf('=');
                if (eq > 0)
                {
                    map._values[arg[2..eq]] = arg[(eq + 1)..];
                    pendingKey = null;
                }
                else
                {
                    pendingKey = arg[2..];
                }
            }
            else if (pendingKey is not null)
            {
                map._values[pendingKey] = arg;
                pendingKey = null;
            }
            else
            {
                map._positional.Add(arg);
            }
        }

        if (pendingKey is not null)
        {
            map._values[pendingKey] = null;
        }

        return map;
    }

    public IReadOnlyList<string> Positional => _positional;

    public bool Has(string key) => _values.ContainsKey(key);

    public string? Get(string key) => _values.GetValueOrDefault(key);

    public string GetRequired(string key) =>
        _values.TryGetValue(key, out string? v) && v is not null
            ? v
            : throw new CliException($"Missing required option --{key}.");

    public string GetOr(string key, string fallback) => _values.GetValueOrDefault(key) ?? fallback;

    public int GetInt(string key, int fallback) =>
        _values.TryGetValue(key, out string? v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)
            ? i
            : fallback;

    public long GetLong(string key, long fallback) =>
        _values.TryGetValue(key, out string? v) && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out long i)
            ? i
            : fallback;

    public double GetDouble(string key, double fallback) =>
        _values.TryGetValue(key, out string? v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
            ? d
            : fallback;

    public bool GetFlag(string key) =>
        _values.TryGetValue(key, out string? v) && (v is null || v.Equals("true", StringComparison.OrdinalIgnoreCase));
}

internal sealed class CliException : Exception
{
    public CliException(string message) : base(message)
    {
    }
}
