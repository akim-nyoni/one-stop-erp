using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>An eviction case on the register.</summary>
/// <param name="CaseNumber">The case number.</param>
/// <param name="TenancyNumber">The tenancy.</param>
/// <param name="UnitCode">The unit.</param>
/// <param name="ArrearsAtOpening">What was owed when it opened.</param>
/// <param name="Stage">The latest stage.</param>
/// <param name="StageDate">When it was reached.</param>
/// <param name="Status">Where the case stands.</param>
public sealed record EvictionCaseEntry(string CaseNumber, string TenancyNumber, string UnitCode, decimal ArrearsAtOpening, EvictionStage Stage, DateOnly StageDate, EvictionCaseStatus Status);

/// <summary>
/// The eviction workflow for council tenancies (brief §4.10.7): open a case for a tenancy in arrears with a reminder,
/// record each later stage in order with the notice period honoured, end with eviction - which ends the tenancy, settles
/// the deposit and returns the unit as void - or withdraw the case.
/// </summary>
public interface IEvictionService
{
    /// <summary>Opens a case with a reminder; the tenancy must be active and owe money, with no other open case.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="tenancyNumber">The tenancy.</param>
    /// <param name="date">The reminder date.</param>
    /// <param name="noticePeriodDays">Days between notice to vacate and legal handover.</param>
    /// <param name="userId">Who opens it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The case number, or why it was refused.</returns>
    Task<Result<string>> OpenAsync(long companyId, string tenancyNumber, DateOnly date, int noticePeriodDays, long userId, CancellationToken ct = default);

    /// <summary>Records the next stage; eviction ends the tenancy, refunding any deposit balance to the bank account given.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="caseNumber">The case.</param>
    /// <param name="stage">The stage reached.</param>
    /// <param name="date">When.</param>
    /// <param name="reference">The letter, notice, attorney or court-order reference.</param>
    /// <param name="refundBankAccountCode">For eviction: the bank account any deposit refund is paid from.</param>
    /// <param name="userId">Who records it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> AdvanceAsync(long companyId, string caseNumber, EvictionStage stage, DateOnly date, string? reference, string? refundBankAccountCode, long userId, CancellationToken ct = default);

    /// <summary>Withdraws an open case.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="caseNumber">The case.</param>
    /// <param name="reason">Why (required).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> WithdrawAsync(long companyId, string caseNumber, string reason, CancellationToken ct = default);

    /// <summary>The eviction register, open cases only if asked, oldest first.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="openOnly">Only open cases.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cases.</returns>
    Task<IReadOnlyList<EvictionCaseEntry>> RegisterAsync(long companyId, bool openOnly, CancellationToken ct = default);
}
