using System.Text.Json;
using System.Text.Json.Serialization;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;

namespace LabelMaker.Core.Serialization;

/// <summary>
/// Reads/writes the polymorphic <see cref="LabelObject"/> hierarchy using a fixed
/// <c>"kind"</c> discriminator and a <b>closed</b> type switch. There is no
/// reflection-based type-name binding, so deserializing a project can never
/// instantiate an arbitrary type or run code (spec §28).
/// </summary>
public sealed class LabelObjectJsonConverter : JsonConverter<LabelObject>
{
    public override LabelObject Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;

        if (!root.TryGetProperty("kind", out JsonElement kindElement))
        {
            throw new JsonException("Label object is missing its 'kind' discriminator.");
        }

        string kind = kindElement.GetString() ?? throw new JsonException("Null 'kind' discriminator.");
        string raw = root.GetRawText();

        LabelObject result = kind switch
        {
            nameof(ObjectKind.Text) => Deserialize<TextObject>(raw, options),
            nameof(ObjectKind.Image) => Deserialize<ImageObject>(raw, options),
            nameof(ObjectKind.Shape) => Deserialize<ShapeObject>(raw, options),
            nameof(ObjectKind.Barcode) => Deserialize<BarcodeObject>(raw, options),
            nameof(ObjectKind.Table) => Deserialize<TableObject>(raw, options),
            _ => throw new JsonException($"Unknown label object kind '{kind}'."),
        };

        return result;
    }

    public override void Write(Utf8JsonWriter writer, LabelObject value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", value.Kind.ToString());

        // Serialize the concrete type, then splice its properties in (minus a
        // duplicate "kind" if the concrete serializer emitted one).
        JsonSerializerOptions inner = CloneWithout(options, this);
        using JsonDocument concrete = JsonSerializer.SerializeToDocument(value, value.GetType(), inner);
        foreach (JsonProperty property in concrete.RootElement.EnumerateObject())
        {
            if (property.NameEquals("kind"))
            {
                continue;
            }

            property.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    private static T Deserialize<T>(string raw, JsonSerializerOptions options) where T : LabelObject
    {
        JsonSerializerOptions inner = CloneWithout(options, null);
        return JsonSerializer.Deserialize<T>(raw, inner)
               ?? throw new JsonException($"Could not read a {typeof(T).Name}.");
    }

    private static JsonSerializerOptions CloneWithout(JsonSerializerOptions source, JsonConverter? exclude)
    {
        var clone = new JsonSerializerOptions(source);
        for (int i = clone.Converters.Count - 1; i >= 0; i--)
        {
            if (clone.Converters[i] is LabelObjectJsonConverter && (exclude is null || ReferenceEquals(clone.Converters[i], exclude)))
            {
                clone.Converters.RemoveAt(i);
            }
        }

        return clone;
    }
}
