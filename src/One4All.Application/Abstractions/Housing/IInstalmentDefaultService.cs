using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>An agreement in arrears and where its default stands.</summary>
/// <param name="AgreementNumber">The agreement.</param>
/// <param name="Buyer">The buyer.</param>
/// <param name="Arrears">The arrears (the buyer's).</param>
/// <param name="DaysOverdue">How long the oldest unpaid instalment has been due.</param>
/// <param name="LastStage">The last notice issued, if any.</param>
/// <param name="NextStage">The next notice due, if one is.</param>
/// <param name="Why">Why no notice is due, when none is.</param>
public sealed record DefaultCase(string AgreementNumber, string Buyer, decimal Arrears, int DaysOverdue, DefaultStage? LastStage, DefaultStage? NextStage, string? Why);

/// <summary>What a penalty interest run charged.</summary>
/// <param name="Agreements">Agreements charged.</param>
/// <param name="Total">The penalty interest.</param>
public sealed record PenaltyRun(int Agreements, decimal Total);

/// <summary>
/// Instalment default (brief §4.10.5 "Default &amp; cancellation: arrears stages → reminder → demand letter → notice of breach → notice of cancellation …
/// every stage a dated document with proof of service", and "penalty interest on arrears").
/// </summary>
public interface IInstalmentDefaultService
{
    /// <summary>The active agreements in arrears with the next notice due.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="asAt">The date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cases, longest overdue first.</returns>
    Task<IReadOnlyList<DefaultCase>> ReviewAsync(long companyId, DateOnly asAt, CancellationToken ct = default);

    /// <summary>Issues the next notice due on an agreement (refused when none is).</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="agreementNumber">The agreement.</param>
    /// <param name="asAt">The date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The notice number and text, or why none is due.</returns>
    Task<Result<(string NoticeNumber, string Text)>> IssueNextAsync(long companyId, string agreementNumber, DateOnly asAt, CancellationToken ct = default);

    /// <summary>Records a notice's proof of service.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="noticeNumber">The notice.</param>
    /// <param name="servedOn">When.</param>
    /// <param name="method">How (hand, registered post, email…).</param>
    /// <param name="servedBy">Who served it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> RecordServiceAsync(long companyId, string noticeNumber, DateOnly servedOn, string method, string servedBy, CancellationToken ct = default);

    /// <summary>Sets an agreement's penalty interest rate on arrears (annual %; 0 for none).</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="agreementNumber">The agreement.</param>
    /// <param name="annualRatePercent">The rate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> SetPenaltyRateAsync(long companyId, string agreementNumber, decimal annualRatePercent, CancellationToken ct = default);

    /// <summary>
    /// Charges a month's penalty interest on each agreement's arrears at the start of the month (invoiced to the buyer, to the agreement's interest account;
    /// once a month per agreement).
    /// </summary>
    /// <param name="companyId">The council.</param>
    /// <param name="month">Any day in the month.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was charged.</returns>
    Task<Result<PenaltyRun>> ChargePenaltyInterestAsync(long companyId, DateOnly month, CancellationToken ct = default);
}
