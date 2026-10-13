using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using One4All.Application.Abstractions.Reporting;

namespace One4All.Infrastructure.Reporting;

/// <summary>A JSON web API a report may read (configuration section <c>Reporting:Apis</c>; keep keys in a secret store).</summary>
public sealed class ReportApiEndpoint
{
    /// <summary>Gets or sets the base address the report's path is added to.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the authentication header name, if any.</summary>
    public string? AuthHeader { get; set; }

    /// <summary>Gets or sets the authentication header value.</summary>
    public string? AuthValue { get; set; }
}

/// <summary>The web APIs reports may read, by name.</summary>
public sealed class ReportApiOptions
{
    /// <summary>Gets the endpoints by name (case-insensitive).</summary>
    public Dictionary<string, ReportApiEndpoint> Endpoints { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Rows for reports whose data source is a web API (brief §13.1 "API data source"): the command "api:NAME/path?x={Param}" calls the named endpoint
/// with the report's parameters filled in, and reads a JSON array of objects (or the array under "value", "data", "items" or "rows"); each
/// property becomes a field. Only configured endpoints are called.
/// </summary>
public sealed class ApiReportDataProvider : IReportDataProvider
{
    private const string Prefix = "api:";
    private static readonly string[] Wrappers = ["value", "data", "items", "rows", "results"];
    private readonly HttpClient _http;
    private readonly ReportApiOptions _options;

    /// <summary>Creates the provider.</summary>
    /// <param name="http">The HTTP client.</param>
    /// <param name="options">The endpoints reports may call.</param>
    public ApiReportDataProvider(HttpClient http, ReportApiOptions options)
    {
        _http = http;
        _options = options;
    }

    /// <inheritdoc />
    public bool CanHandle(string command) => command.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(parameters);
        string target = command[Prefix.Length..].Trim();
        int slash = target.IndexOfAny(['/', '?']);
        string name = slash < 0 ? target : target[..slash], path = slash < 0 ? string.Empty : target[slash..];
        if (!_options.Endpoints.TryGetValue(name, out ReportApiEndpoint? endpoint) || string.IsNullOrWhiteSpace(endpoint.BaseUrl))
        {
            throw new InvalidOperationException($"No report API endpoint named {name} is configured (Reporting:Apis).");
        }

        foreach ((string key, object? value) in parameters)
        {
            path = path.Replace("{" + key + "}", Uri.EscapeDataString(Text(value)), StringComparison.OrdinalIgnoreCase);
        }

        using HttpRequestMessage request = new(HttpMethod.Get, endpoint.BaseUrl.TrimEnd('/') + path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(endpoint.AuthHeader))
        {
            request.Headers.TryAddWithoutValidation(endpoint.AuthHeader, endpoint.AuthValue);
        }

        using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using JsonDocument json = await JsonDocument.ParseAsync(body, cancellationToken: ct).ConfigureAwait(false);
        JsonElement rows = json.RootElement;
        if (rows.ValueKind == JsonValueKind.Object)
        {
            rows = Wrappers.Select(w => rows.TryGetProperty(w, out JsonElement inner) ? inner : default).FirstOrDefault(e => e.ValueKind == JsonValueKind.Array);
        }

        if (rows.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<IReadOnlyDictionary<string, object?>> result = [];
        foreach (JsonElement item in rows.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object))
        {
            Dictionary<string, object?> row = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in item.EnumerateObject())
            {
                row[property.Name] = Value(property.Value);
            }

            result.Add(row);
        }

        return result;
    }

    private static object? Value(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.TryGetDecimal(out decimal number) ? number : value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => value.GetRawText(),
    };

    private static string Text(object? value) => value switch
    {
        null => string.Empty,
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
