using One4All.Core.Common;

namespace One4All.Core.FixedAssets;

/// <summary>
/// A record of a fixed-asset disposal (<c>fa.asset_disposal</c>), written when an asset is disposed: the date,
/// proceeds, net book value at disposal and the resulting gain or loss, plus the GL journal. It preserves the
/// disposal history for the disposals report after the asset itself is marked disposed.
/// </summary>
public sealed class AssetDisposal : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private AssetDisposal()
    {
        AssetNumber = string.Empty;
        JournalNumber = string.Empty;
    }

    /// <summary>Records a disposal.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="assetId">The disposed asset.</param>
    /// <param name="assetNumber">The asset number (kept for the report).</param>
    /// <param name="date">The disposal date.</param>
    /// <param name="proceeds">The disposal proceeds.</param>
    /// <param name="netBookValue">The net book value at disposal.</param>
    /// <param name="gainOrLoss">Proceeds − net book value (positive is a gain).</param>
    /// <param name="journalNumber">The disposal GL journal number.</param>
    public AssetDisposal(long companyId, long assetId, string assetNumber, DateOnly date, decimal proceeds, decimal netBookValue, decimal gainOrLoss, string journalNumber)
    {
        CompanyId = companyId;
        AssetId = assetId;
        AssetNumber = assetNumber;
        Date = date;
        Proceeds = proceeds;
        NetBookValue = netBookValue;
        GainOrLoss = gainOrLoss;
        JournalNumber = journalNumber;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The disposed asset.</summary>
    public long AssetId { get; private set; }

    /// <summary>The asset number (kept for the report).</summary>
    public string AssetNumber { get; private set; }

    /// <summary>The disposal date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The disposal proceeds.</summary>
    public decimal Proceeds { get; private set; }

    /// <summary>The net book value at disposal.</summary>
    public decimal NetBookValue { get; private set; }

    /// <summary>The gain (positive) or loss (negative) on disposal.</summary>
    public decimal GainOrLoss { get; private set; }

    /// <summary>The disposal GL journal number.</summary>
    public string JournalNumber { get; private set; }
}
