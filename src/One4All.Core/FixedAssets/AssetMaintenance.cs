using One4All.Core.Common;

namespace One4All.Core.FixedAssets;

/// <summary>
/// A maintenance/service event logged against a fixed asset (<c>fa.asset_maintenance</c>): date, what was done,
/// the provider and an informational cost. Operational asset-lifecycle history — it holds no GL balance (the
/// actual spend is captured through the supplier invoice or expense claim that pays for it).
/// </summary>
public sealed class AssetMaintenance : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private AssetMaintenance() => Description = string.Empty;

    /// <summary>Logs a maintenance event.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="assetId">The asset serviced.</param>
    /// <param name="date">The service date.</param>
    /// <param name="description">What was done.</param>
    /// <param name="cost">The informational cost.</param>
    /// <param name="provider">The service provider, if any.</param>
    public AssetMaintenance(long companyId, long assetId, DateOnly date, string description, decimal cost, string? provider)
    {
        CompanyId = companyId;
        AssetId = assetId;
        Date = date;
        Description = description;
        Cost = cost;
        Provider = provider;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The asset serviced.</summary>
    public long AssetId { get; private set; }

    /// <summary>The service date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>What was done.</summary>
    public string Description { get; private set; }

    /// <summary>The informational cost of the work.</summary>
    public decimal Cost { get; private set; }

    /// <summary>The service provider, if any.</summary>
    public string? Provider { get; private set; }
}
