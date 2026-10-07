using One4All.Core.Common;

namespace One4All.Application.Abstractions.JobCosting;

/// <summary>One line of a BOQ import, with what is wrong with it.</summary>
/// <param name="Line">The line number in the file.</param>
/// <param name="ItemCode">The BOQ item reference.</param>
/// <param name="Description">The work.</param>
/// <param name="Unit">The unit.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="Rate">The rate.</param>
/// <param name="CostHead">The cost head, if given.</param>
/// <param name="Error">Why it cannot be imported (null: fine).</param>
public sealed record BoqImportLine(int Line, string ItemCode, string Description, string Unit, decimal? Quantity, decimal? Rate, string? CostHead, string? Error);

/// <summary>A BOQ import checked before anything is saved.</summary>
/// <param name="Lines">Every line.</param>
public sealed record BoqImportPreview(IReadOnlyList<BoqImportLine> Lines)
{
    /// <summary>Lines with errors.</summary>
    public int ErrorCount => Lines.Count(l => l.Error is not null);

    /// <summary>The value of the lines (quantity × rate).</summary>
    public decimal TotalValue => Lines.Where(l => l.Error is null).Sum(l => decimal.Round((l.Quantity ?? 0m) * (l.Rate ?? 0m), 2, MidpointRounding.AwayFromZero));
}

/// <summary>A cost head's BOQ budget against what has been measured and spent.</summary>
/// <param name="CostHead">The cost head ("(none)" for BOQ lines without one).</param>
/// <param name="BoqValue">The BOQ value.</param>
/// <param name="MeasuredValue">The value measured to date.</param>
/// <param name="ActualCost">Cost captured under the head.</param>
public sealed record BoqCostHeadRow(string CostHead, decimal BoqValue, decimal MeasuredValue, decimal ActualCost);

/// <summary>Contract scope and measurement controls (brief §14 K 191, 193, 196): variations, the bill of quantities and timesheets.</summary>
public interface IContractControlService
{
    /// <summary>Submits a variation order on an open contract.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="contractId">The contract.</param>
    /// <param name="number">The VO number (unique per contract).</param>
    /// <param name="description">What changes.</param>
    /// <param name="valueImpact">The change to contract value.</param>
    /// <param name="costImpact">The change to budgeted cost.</param>
    /// <param name="date">When it was submitted.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The variation id, or an error.</returns>
    Task<Result<long>> SubmitVariationAsync(long companyId, long contractId, string number, string description, decimal valueImpact, decimal costImpact, DateOnly date, CancellationToken ct = default);

    /// <summary>Approves or rejects a submitted variation; approval moves the contract value and budget by its impacts.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="variationId">The variation.</param>
    /// <param name="approve">Approve (true) or reject.</param>
    /// <param name="date">When.</param>
    /// <param name="userId">Who decided.</param>
    /// <param name="note">Any note.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or an error.</returns>
    Task<Result> DecideVariationAsync(long companyId, long variationId, bool approve, DateOnly date, long? userId, string? note, CancellationToken ct = default);

    /// <summary>Adds a bill-of-quantities line to a contract.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="contractId">The contract.</param>
    /// <param name="itemCode">The BOQ reference (unique per contract).</param>
    /// <param name="description">The work.</param>
    /// <param name="unit">The unit.</param>
    /// <param name="quantity">The billed quantity.</param>
    /// <param name="rate">The rate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The BOQ line id, or an error.</returns>
    Task<Result<long>> AddBoqItemAsync(long companyId, long contractId, string itemCode, string description, string unit, decimal quantity, decimal rate, CancellationToken ct = default);

    /// <summary>
    /// Checks a BOQ file (CSV: item, description, unit, quantity, rate[, cost head]; a header row and quoted fields allowed) against the contract
    /// without saving anything: each line with its error — a missing field, a bad number, an item repeated in the file or already on the contract.
    /// </summary>
    Task<Result<BoqImportPreview>> PreviewBoqImportAsync(long companyId, long contractId, string csv, CancellationToken ct = default);

    /// <summary>Imports a BOQ file: all lines or, when any line has an error, none (brief §4.17 BOQ import).</summary>
    Task<Result<int>> ImportBoqAsync(long companyId, long contractId, string csv, CancellationToken ct = default);

    /// <summary>The contract's BOQ budget by cost head against the value measured and the cost captured under each head.</summary>
    Task<Result<IReadOnlyList<BoqCostHeadRow>>> BoqByCostHeadAsync(long companyId, long contractId, CancellationToken ct = default);

    /// <summary>Records the quantity measured to date against a BOQ line.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="boqItemId">The BOQ line.</param>
    /// <param name="quantityToDate">The cumulative measured quantity.</param>
    /// <param name="date">When measured.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or an error.</returns>
    Task<Result> MeasureAsync(long companyId, long boqItemId, decimal quantityToDate, DateOnly date, CancellationToken ct = default);

    /// <summary>Issues stock to a contract: the goods leave the warehouse charged to work in progress, and the job's materials head takes their cost.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="contractId">The contract.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="warehouseId">The warehouse or site store.</param>
    /// <param name="date">The issue date.</param>
    /// <param name="quantity">The quantity.</param>
    /// <param name="workInProgressAccountCode">The contract WIP account charged.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cost issued, or an error.</returns>
    Task<Result<decimal>> IssueMaterialsAsync(long companyId, long contractId, long itemId, long warehouseId, DateOnly date, decimal quantity, string workInProgressAccountCode, CancellationToken ct = default);

    /// <summary>Records an employee's hours on a contract and charges their cost to the job's labour cost head.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="contractId">The contract.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="date">The day.</param>
    /// <param name="hours">The hours (0–24).</param>
    /// <param name="hourlyCost">The cost per hour.</param>
    /// <param name="costHead">The cost head (default Labour).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The timesheet id, or an error.</returns>
    Task<Result<long>> RecordTimeAsync(long companyId, long contractId, long employeeId, DateOnly date, decimal hours, decimal hourlyCost, string? costHead = null, CancellationToken ct = default);
}
