using One4All.Core.Common;

namespace One4All.Application.Abstractions.Budgeting;

/// <summary>A virement request.</summary>
/// <param name="FromAccountCode">The line giving up budget.</param>
/// <param name="ToAccountCode">The line receiving it.</param>
/// <param name="Period">The period the budget moves in.</param>
/// <param name="Amount">The amount; positive.</param>
/// <param name="Justification">Why; required.</param>
/// <param name="FromCostCentre">The cost centre giving up budget (null: the line without one).</param>
/// <param name="ToCostCentre">The cost centre receiving it.</param>
/// <param name="ToBudgetId">Another department's budget receiving it (same year and type), or null for the same budget.</param>
public sealed record VirementRequest(string FromAccountCode, string ToAccountCode, int Period, decimal Amount, string Justification, string? FromCostCentre = null, string? ToCostCentre = null,
    long? ToBudgetId = null);

/// <summary>One account on the virement register.</summary>
/// <param name="AccountCode">The account.</param>
/// <param name="Original">The budget as approved.</param>
/// <param name="VirementsIn">Approved virements into the line.</param>
/// <param name="VirementsOut">Approved virements out of it.</param>
public sealed record VirementRegisterRow(string AccountCode, decimal Original, decimal VirementsIn, decimal VirementsOut)
{
    /// <summary>The current budget: original plus virements in less virements out.</summary>
    public decimal Current => Original + VirementsIn - VirementsOut;
}

/// <summary>A budget line that has crossed an alert threshold.</summary>
/// <param name="AccountCode">The vote (account).</param>
/// <param name="Name">Its name.</param>
/// <param name="Appropriation">The year's appropriation.</param>
/// <param name="Used">Expenditure plus open commitments to the as-at period.</param>
/// <param name="PercentUsed">The percentage of the appropriation used.</param>
/// <param name="ThresholdCrossed">The highest alert threshold reached.</param>
public sealed record UtilisationAlert(string AccountCode, string Name, decimal Appropriation, decimal Used, decimal PercentUsed, decimal ThresholdCrossed);

/// <summary>A commitment as decided at year end.</summary>
/// <param name="CommitmentId">The year's commitment.</param>
/// <param name="AccountCode">Its account.</param>
/// <param name="Reference">Its source reference.</param>
/// <param name="Amount">Its amount.</param>
/// <param name="CarriedToId">The next year's commitment it became, when carried forward.</param>
public sealed record YearEndCommitment(long CommitmentId, string AccountCode, string Reference, decimal Amount, long? CarriedToId);

/// <summary>The year-end decision on a year's open commitments.</summary>
/// <param name="Lapsed">Commitments that lapsed with the year.</param>
/// <param name="CarriedForward">Commitments carried into the next year.</param>
public sealed record CommitmentYearEnd(IReadOnlyList<YearEndCommitment> Lapsed, IReadOnlyList<YearEndCommitment> CarriedForward);

/// <summary>One vote in the vote book: the year's appropriation against expenditure and commitments to the as-at period.</summary>
/// <param name="AccountCode">The vote (account).</param>
/// <param name="Name">Its name.</param>
/// <param name="Original">The original approved appropriation.</param>
/// <param name="Supplementary">Supplementary appropriations.</param>
/// <param name="Virements">Net approved virements (in less out).</param>
/// <param name="Expenditure">Expenditure posted.</param>
/// <param name="Commitments">Open commitments (orders not yet invoiced).</param>
/// <param name="Fund">The fund (vote) the row is for, in a vote book kept by fund; null for the account across funds.</param>
public sealed record VoteBookRow(string AccountCode, string Name, decimal Original, decimal Supplementary, decimal Virements, decimal Expenditure, decimal Commitments, string? Fund = null)
{
    /// <summary>The current appropriation.</summary>
    public decimal Appropriation => Original + Supplementary + Virements;

    /// <summary>What is left to spend: appropriation less expenditure and commitments.</summary>
    public decimal Unexpended => Appropriation - Expenditure - Commitments;
}

/// <summary>
/// Budget control for councils and grant-funded bodies (brief §4.18.4–4.18.5): virements between budget lines, requested
/// with a justification and approved by a different user, never exceeding what the source line has left; the virement
/// register; and the vote book - appropriation, expenditure, commitments and unexpended balance per vote.
/// </summary>
public interface IBudgetControlService
{
    /// <summary>Requests a virement within an active budget.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="budgetId">The budget.</param>
    /// <param name="request">The virement.</param>
    /// <param name="requestedByUserId">Who asks.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The virement id, or why it was refused.</returns>
    Task<Result<long>> RequestVirementAsync(long companyId, long budgetId, VirementRequest request, long requestedByUserId, CancellationToken ct = default);

    /// <summary>Approves a virement and applies it to the budget; never by the requester, and only while the source still has the budget.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="virementId">The virement.</param>
    /// <param name="approverUserId">The approver.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> ApproveVirementAsync(long companyId, long virementId, long approverUserId, CancellationToken ct = default);

    /// <summary>Replaces the company's virement approval bands (none: any other user may approve).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="bands">The bands.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or what is wrong with the bands.</returns>
    Task<Result> SetVirementBandsAsync(long companyId, IReadOnlyList<One4All.Application.Budgeting.VirementBandRule> bands, CancellationToken ct = default);

    /// <summary>Rejects a virement.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="virementId">The virement.</param>
    /// <param name="userId">Who rejects it.</param>
    /// <param name="reason">Why; required.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> RejectVirementAsync(long companyId, long virementId, long userId, string reason, CancellationToken ct = default);

    /// <summary>The virement register for a budget: original, virements in and out, and current, per account.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="budgetId">The budget.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The register, by account.</returns>
    Task<Result<IReadOnlyList<VirementRegisterRow>>> VirementRegisterAsync(long companyId, long budgetId, CancellationToken ct = default);

    /// <summary>
    /// The vote book for a year: each vote's appropriation for the year, across the active approved and supplementary
    /// budgets and approved virements, against expenditure and open commitments to the as-at period.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fiscalYearId">The fiscal year.</param>
    /// <param name="asAtPeriod">The last period included (1–12).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="byFund">One row per account and fund the budget appropriates (the account's unfunded lines and other spend in a row with no fund), instead of per account.</param>
    /// <returns>One row per vote, by account (and fund).</returns>
    Task<IReadOnlyList<VoteBookRow>> VoteBookAsync(long companyId, long fiscalYearId, int asAtPeriod, CancellationToken ct = default, bool byFund = false);

    /// <summary>
    /// Settles a year's open commitments at year end (brief §4.18.5): those chosen are carried forward into commitments
    /// against the next year's budget, linked to their originals; the rest lapse. The decision records who approved it.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fiscalYearId">The year ending.</param>
    /// <param name="nextFiscalYearId">The following year.</param>
    /// <param name="carryForwardIds">The commitments to carry forward; each must be open in the year ending.</param>
    /// <param name="approvedByUserId">Who approved the decision.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What lapsed and what was carried, or why it was refused.</returns>
    Task<Result<CommitmentYearEnd>> CloseYearCommitmentsAsync(long companyId, long fiscalYearId, long nextFiscalYearId, IReadOnlyCollection<long> carryForwardIds, long approvedByUserId, CancellationToken ct = default);

    /// <summary>
    /// The budget-holder alerts (brief §4.18.5): each vote whose expenditure and commitments have reached one of the
    /// utilisation thresholds of its appropriation, with the highest reached, most used first.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fiscalYearId">The fiscal year.</param>
    /// <param name="asAtPeriod">The last period included.</param>
    /// <param name="thresholdPercents">The alert thresholds, such as 75, 90 and 100.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The alerts.</returns>
    Task<IReadOnlyList<UtilisationAlert>> UtilisationAlertsAsync(long companyId, long fiscalYearId, int asAtPeriod, IReadOnlyList<decimal> thresholdPercents, CancellationToken ct = default);

    /// <summary>Checks an approved budget's lines against its seal.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="budgetId">The budget.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when the lines are as approved; an error when the budget is unknown or was never sealed.</returns>
    Task<Result<bool>> VerifySealAsync(long companyId, long budgetId, CancellationToken ct = default);
}
