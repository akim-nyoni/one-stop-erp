using System.Text.Json.Serialization;

namespace One4All.Licensing.Core;

/// <summary>The signed licence payload (brief §11.3). Serialized to canonical JSON for signing.</summary>
public sealed class LicensePayload
{
    /// <summary>Unique licence identifier.</summary>
    [JsonPropertyName("licence_id")]
    public string LicenceId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Key id of the signing key (supports key rotation).</summary>
    [JsonPropertyName("kid")]
    public string KeyId { get; set; } = string.Empty;

    /// <summary>When the licence was issued (UTC, ISO 8601).</summary>
    [JsonPropertyName("issued_at")]
    public DateTimeOffset IssuedAt { get; set; }

    /// <summary>The licensed customer.</summary>
    [JsonPropertyName("customer")]
    public LicenceCustomer Customer { get; set; } = new();

    /// <summary>Product name (must match the ERP's expected product string).</summary>
    [JsonPropertyName("product")]
    public string Product { get; set; } = "One4All ERP";

    /// <summary>The edition.</summary>
    [JsonPropertyName("edition")]
    public LicenceEdition Edition { get; set; }

    /// <summary>The licence type.</summary>
    [JsonPropertyName("licence_type")]
    public LicenceType LicenceType { get; set; }

    /// <summary>Start of validity (date).</summary>
    [JsonPropertyName("valid_from")]
    public DateOnly ValidFrom { get; set; }

    /// <summary>End of validity (date); null for perpetual.</summary>
    [JsonPropertyName("valid_to")]
    public DateOnly? ValidTo { get; set; }

    /// <summary>Days of grace after expiry before enforcement bites.</summary>
    [JsonPropertyName("grace_days")]
    public int GraceDays { get; set; }

    /// <summary>When true, past grace the product hard-stops; when false it degrades to read-only.</summary>
    [JsonPropertyName("hard_stop")]
    public bool HardStop { get; set; } = true;

    /// <summary>Machine binding rules.</summary>
    [JsonPropertyName("machine_binding")]
    public MachineBinding MachineBinding { get; set; } = new();

    /// <summary>Global limits (companies, users, terminals, properties, students, employees, …).</summary>
    [JsonPropertyName("limits")]
    public Dictionary<string, int?> Limits { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The licensed modules with their own expiry and limits.</summary>
    [JsonPropertyName("modules")]
    public List<LicenceModuleGrant> Modules { get; set; } = [];

    /// <summary>Feature flags (e.g. MULTI_CURRENCY, OFFLINE_POS, API_ACCESS).</summary>
    [JsonPropertyName("features")]
    public List<string> Features { get; set; } = [];

    /// <summary>Support expiry date, if any.</summary>
    [JsonPropertyName("support_expiry")]
    public DateOnly? SupportExpiry { get; set; }

    /// <summary>Free-form notes.</summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

/// <summary>The customer a licence is issued to.</summary>
public sealed class LicenceCustomer
{
    /// <summary>Customer id in the vendor's system.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Customer name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Contact detail.</summary>
    [JsonPropertyName("contact")]
    public string? Contact { get; set; }
}

/// <summary>Machine-binding configuration for a licence.</summary>
public sealed class MachineBinding
{
    /// <summary>The binding mode.</summary>
    [JsonPropertyName("mode")]
    public MachineBindingMode Mode { get; set; } = MachineBindingMode.None;

    /// <summary>Accepted machine fingerprints (allow several for server failover).</summary>
    [JsonPropertyName("fingerprints")]
    public List<string> Fingerprints { get; set; } = [];

    /// <summary>How many times the licence may be re-bound to new hardware.</summary>
    [JsonPropertyName("allow_rebind_count")]
    public int AllowRebindCount { get; set; }
}

/// <summary>A per-module grant with its own expiry and limits (brief §11.3).</summary>
public sealed class LicenceModuleGrant
{
    /// <summary>The module code (e.g. GL, AR, POS, MUNI, HSE, BUD, PAY).</summary>
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    /// <summary>Module expiry date; null means it follows the licence / is perpetual.</summary>
    [JsonPropertyName("valid_to")]
    public DateOnly? ValidTo { get; set; }

    /// <summary>Module-specific limits (e.g. terminals, properties, employees).</summary>
    [JsonPropertyName("limits")]
    public Dictionary<string, int?> Limits { get; set; } = new(StringComparer.Ordinal);
}
