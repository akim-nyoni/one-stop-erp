using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace One4All.Licensing.Core;

/// <summary>
/// Produces a deterministic (canonical) JSON encoding of a licence payload so that signing and
/// verification operate on identical bytes regardless of property ordering or formatting. Object keys
/// are sorted ordinally and all insignificant whitespace is removed.
/// </summary>
public static class CanonicalJson
{
    /// <summary>The JSON options used to (de)serialize licence payloads (enums as strings, ISO dates).</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = false,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    /// <summary>Serializes a payload to canonical UTF-8 JSON bytes.</summary>
    /// <param name="payload">The licence payload.</param>
    /// <returns>Canonical UTF-8 bytes.</returns>
    public static byte[] SerializeToBytes(LicensePayload payload)
    {
        string json = JsonSerializer.Serialize(payload, Options);
        using JsonDocument document = JsonDocument.Parse(json);
        StringBuilder sb = new();
        WriteCanonical(document.RootElement, sb);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>Deserializes a payload from its JSON form.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The payload, or null if it cannot be parsed.</returns>
    public static LicensePayload? Deserialize(string json) =>
        JsonSerializer.Deserialize<LicensePayload>(json, Options);

    /// <summary>Serializes a payload to (non-canonical, human-readable) indented JSON for display/inspection.</summary>
    /// <param name="payload">The payload.</param>
    /// <returns>Indented JSON.</returns>
    public static string ToReadableJson(LicensePayload payload)
    {
        JsonSerializerOptions readable = new(Options) { WriteIndented = true };
        return JsonSerializer.Serialize(payload, readable);
    }

    private static void WriteCanonical(JsonElement element, StringBuilder sb)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                sb.Append('{');
                bool firstProp = true;
                foreach (JsonProperty property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (!firstProp)
                    {
                        sb.Append(',');
                    }

                    firstProp = false;
                    sb.Append(JsonSerializer.Serialize(property.Name)).Append(':');
                    WriteCanonical(property.Value, sb);
                }

                sb.Append('}');
                break;

            case JsonValueKind.Array:
                sb.Append('[');
                bool firstItem = true;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (!firstItem)
                    {
                        sb.Append(',');
                    }

                    firstItem = false;
                    WriteCanonical(item, sb);
                }

                sb.Append(']');
                break;

            default:
                sb.Append(element.GetRawText());
                break;
        }
    }
}
