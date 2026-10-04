using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A levy raised.</summary>
/// <param name="LevyCode">The levy.</param>
/// <param name="Amount">The amount.</param>
/// <param name="DocumentNumber">The invoice.</param>
public sealed record LevyRaised(string LevyCode, decimal Amount, string DocumentNumber);

/// <summary>
/// Development and infrastructure levies (brief §4.10.2): configurable charge types — flat, per square metre or a
/// percentage of the price — raised on the sales ledger when an allocation is accepted (on the beneficiary, at the price
/// frozen at offer) or on a change of ownership (on the incoming holder, on the consideration); each levy is raised once
/// per event, however often the trigger is run.
/// </summary>
public interface IDevelopmentLevyService
{
    /// <summary>Defines a levy.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="code">The levy code.</param>
    /// <param name="name">Its name.</param>
    /// <param name="trigger">When it is raised.</param>
    /// <param name="basis">How it is measured.</param>
    /// <param name="rate">The amount, rate per m² or percentage.</param>
    /// <param name="incomeAccountCode">The income account credited.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The levy id, or why it was refused.</returns>
    Task<Result<long>> DefineAsync(long companyId, string code, string name, LevyTrigger trigger, LevyBasis basis, decimal rate, string incomeAccountCode, CancellationToken ct = default);

    /// <summary>Stops a levy being raised.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="code">The levy.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> DeactivateAsync(long companyId, string code, CancellationToken ct = default);

    /// <summary>Raises the allocation levies on an accepted allocation's beneficiary (their customer account is found by national ID).</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="certificateNumber">The allocation certificate.</param>
    /// <param name="date">The invoice date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The levies raised now (none when all were raised before), or why it was refused.</returns>
    Task<Result<IReadOnlyList<LevyRaised>>> ChargeAllocationAsync(long companyId, string certificateNumber, DateOnly date, CancellationToken ct = default);

    /// <summary>Raises the transfer levies on a transfer application's incoming holder.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="applicationNumber">The transfer application.</param>
    /// <param name="date">The invoice date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The levies raised now (none when all were raised before), or why it was refused.</returns>
    Task<Result<IReadOnlyList<LevyRaised>>> ChargeTransferAsync(long companyId, string applicationNumber, DateOnly date, CancellationToken ct = default);
}
