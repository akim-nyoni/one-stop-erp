using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>
/// Rental units held to earn rentals, accounted for as investment property (IAS 40, cost or fair value model) or PPE (IAS 16) - out of the
/// stand inventory pool (brief §4.10.9).
/// </summary>
public interface IRentalPropertyAccountingService
{
    /// <summary>
    /// Designates a unit's treatment and brings it into the asset account at its carrying amount (Dr asset, Cr the contra account - the
    /// account it is transferred from, or opening equity). A unit is designated once.
    /// </summary>
    /// <returns>The journal number (empty when the amount is nil).</returns>
    Task<Result<string>> DesignateAsync(long companyId, string unitCode, RentalPropertyTreatment treatment, string assetAccountCode, decimal carryingAmount, string contraAccountCode, DateOnly on,
        CancellationToken ct = default);

    /// <summary>Remeasures a fair-value investment property: the change goes to the fair-value gain or loss account (profit or loss).</summary>
    /// <returns>The journal number (empty when the value is unchanged).</returns>
    Task<Result<string>> RevalueAsync(long companyId, string unitCode, decimal fairValue, DateOnly on, string gainLossAccountCode, CancellationToken ct = default);

    /// <summary>The designated units with their treatment and carrying amounts.</summary>
    Task<IReadOnlyList<RentalPropertyRow>> RegisterAsync(long companyId, CancellationToken ct = default);
}

/// <summary>A unit in the investment property / PPE register.</summary>
/// <param name="UnitCode">The unit.</param>
/// <param name="Treatment">How it is accounted for.</param>
/// <param name="AssetAccountCode">The asset account.</param>
/// <param name="CarryingAmount">The carrying amount.</param>
/// <param name="LastValuedOn">When it was last valued.</param>
public sealed record RentalPropertyRow(string UnitCode, RentalPropertyTreatment Treatment, string AssetAccountCode, decimal CarryingAmount, DateOnly? LastValuedOn);
