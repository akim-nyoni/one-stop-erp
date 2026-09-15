using System.Text.Json;
using System.Text.Json.Serialization;

namespace One4All.Reporting.Model;

/// <summary>
/// Serializes a <see cref="ReportDefinition"/> to and from the <c>.mrpt</c> JSON format used for storage in
/// <c>rpt.report_definition</c> and for sharing report files between sites (brief §13.1/§13.2).
/// </summary>
public static class ReportDefinitionSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    /// <summary>Serializes a definition to <c>.mrpt</c> JSON.</summary>
    /// <param name="definition">The definition.</param>
    /// <returns>The JSON text.</returns>
    public static string Serialize(ReportDefinition definition) => JsonSerializer.Serialize(definition, Options);

    /// <summary>Deserializes a definition from <c>.mrpt</c> JSON.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The definition.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the JSON is not a valid report definition.</exception>
    public static ReportDefinition Deserialize(string json) =>
        JsonSerializer.Deserialize<ReportDefinition>(json, Options)
        ?? throw new InvalidOperationException("The report definition JSON could not be parsed.");
}
