using One4All.Application.Inventory;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Inventory;

/// <summary>One item-and-warehouse holding on the stock provision report.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="ItemCode">Its code.</param>
/// <param name="Description">Its description.</param>
/// <param name="WarehouseId">The warehouse.</param>
/// <param name="WarehouseCode">Its code.</param>
/// <param name="Quantity">Quantity on hand at the date.</param>
/// <param name="Cost">Carrying cost at the date.</param>
/// <param name="SellingPrice">The default price-list selling price used for NRV, when there is one.</param>
/// <param name="NetRealisableValue">Its net realisable value, when a selling price is known.</param>
/// <param name="DaysSinceLastIssue">Days since stock was last issued (or first received).</param>
/// <param name="Provision">The provision against it.</param>
/// <param name="Basis">Why.</param>
public readonly record struct StockProvisionLine(long ItemId, string ItemCode, string Description, long WarehouseId, string WarehouseCode, decimal Quantity, decimal Cost, decimal? SellingPrice, decimal? NetRealisableValue, int DaysSinceLastIssue, decimal Provision, ProvisionBasis Basis);

/// <summary>The stock provision report as at a date.</summary>
/// <param name="AsAtDate">The date.</param>
/// <param name="Lines">Every holding with stock on hand.</param>
/// <param name="TargetProvision">The provision the policy calls for.</param>
/// <param name="BookedProvision">The provision already on the provision account at the date.</param>
public sealed record StockProvisionReport(DateOnly AsAtDate, IReadOnlyList<StockProvisionLine> Lines, decimal TargetProvision, decimal BookedProvision)
{
    /// <summary>Total carrying cost.</summary>
    public decimal TotalCost => Lines.Sum(l => l.Cost);

    /// <summary>What a posting would charge (positive) or release (negative).</summary>
    public decimal Adjustment => TargetProvision - BookedProvision;
}

/// <summary>The result of posting the stock provision.</summary>
/// <param name="Report">The assessment posted.</param>
/// <param name="JournalNumber">The adjusting journal, or null when the booked provision already matched.</param>
public sealed record StockProvisionPostingResult(StockProvisionReport Report, string? JournalNumber);

/// <summary>
/// Stock provisions (brief §3.6, IAS 2): assesses every holding at the lower of cost and net realisable value, with
/// slow-moving and obsolete stock provided for by age, and books the provision on a contra-stock account against a
/// write-down expense. Quantities and costs are taken from the stock movements as at the date; posting adjusts the
/// provision already booked to the target, so a repeat posts nothing and a release posts back.
/// </summary>
public interface IStockProvisionService
{
    /// <summary>Assesses the provision as at a date without posting.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The date.</param>
    /// <param name="policy">The provisioning policy.</param>
    /// <param name="provisionAccountCode">The contra-stock provision account (to read what is already booked).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The report.</returns>
    Task<Result<StockProvisionReport>> AssessAsync(long companyId, DateOnly asAtDate, StockProvisionPolicy policy, string provisionAccountCode, CancellationToken ct = default);

    /// <summary>Assesses and books the adjustment to bring the provision to target.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The date.</param>
    /// <param name="policy">The provisioning policy.</param>
    /// <param name="provisionAccountCode">The contra-stock provision account (credited as the provision grows).</param>
    /// <param name="writeDownExpenseAccountCode">The write-down expense account (debited as the provision grows).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The posting result.</returns>
    Task<Result<StockProvisionPostingResult>> PostAsync(long companyId, DateOnly asAtDate, StockProvisionPolicy policy, string provisionAccountCode, string writeDownExpenseAccountCode, CancellationToken ct = default);
}
