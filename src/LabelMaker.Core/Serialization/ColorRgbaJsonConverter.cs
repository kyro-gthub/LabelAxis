using System.Text.Json;
using System.Text.Json.Serialization;
using LabelMaker.Core.Model;

namespace LabelMaker.Core.Serialization;

/// <summary>Serializes <see cref="ColorRgba"/> as a compact "#RRGGBB[AA]" string.</summary>
public sealed class ColorRgbaJsonConverter : JsonConverter<ColorRgba>
{
    public override ColorRgba Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            string? text = reader.GetString();
            return ColorRgba.TryParse(text, out ColorRgba c)
                ? c
                : throw new JsonException($"'{text}' is not a colour.");
        }

        throw new JsonException("Expected a colour string like \"#1A2B3C\".");
    }

    public override void Write(Utf8JsonWriter writer, ColorRgba value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToHex());
}
