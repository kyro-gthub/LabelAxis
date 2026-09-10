using System.Text.Json;
using System.Text.Json.Serialization;

namespace LabelMaker.Core.Serialization;

/// <summary>Shared JSON settings for reading/writing the document model. Closed, no polymorphic type binding.</summary>
public static class ProjectJson
{
    public static JsonSerializerOptions CreateOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters =
        {
            new JsonStringEnumConverter(),
            new LabelObjectJsonConverter(),
            new ColorRgbaJsonConverter(),
        },
    };

    public static readonly JsonSerializerOptions Default = CreateOptions();
}
