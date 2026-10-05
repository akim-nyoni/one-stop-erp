using One4All.Core.Common;

namespace One4All.Application.Abstractions.JobCosting;

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

    /// <summary>Records the quantity measured to date against a BOQ line.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="boqItemId">The BOQ line.</param>
    /// <param name="quantityToDate">The cumulative measured quantity.</param>
    /// <param name="date">When measured.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or an error.</returns>
    Task<Result> MeasureAsync(long companyId, long boqItemId, decimal quantityToDate, DateOnly date, CancellationToken ct = default);

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
