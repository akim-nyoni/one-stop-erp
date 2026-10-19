using One4All.Core.Banking;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Banking;

/// <summary>A request to create a bank rule.</summary>
public sealed record CreateBankRuleRequest
{
    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The bank account, or null for every bank account.</summary>
    public long? BankAccountId { get; init; }

    /// <summary>A name for the rule.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The phrase the statement description must contain.</summary>
    public string DescriptionContains { get; init; } = string.Empty;

    /// <summary>The direction filter.</summary>
    public BankRuleDirection Direction { get; init; }

    /// <summary>Optional lower bound on the absolute amount.</summary>
    public decimal? MinAmount { get; init; }

    /// <summary>Optional upper bound on the absolute amount.</summary>
    public decimal? MaxAmount { get; init; }

    /// <summary>The account to post to.</summary>
    public string AccountCode { get; init; } = string.Empty;

    /// <summary>The tax code when the amounts include VAT, or null.</summary>
    public string? TaxCode { get; init; }

    /// <summary>The posting narrative, or null for the bank's description.</summary>
    public string? Narrative { get; init; }

    /// <summary>Lower is tried first.</summary>
    public int Priority { get; init; } = 100;
}

/// <summary>One line a rule run posted.</summary>
/// <param name="StatementLineId">The statement line.</param>
/// <param name="RuleName">The rule that matched.</param>
/// <param name="JournalNumber">The journal posted.</param>
/// <param name="Amount">The signed amount.</param>
public sealed record BankRulePosting(long StatementLineId, string RuleName, string JournalNumber, decimal Amount);

/// <summary>What a rule run did.</summary>
/// <param name="Posted">The lines posted and matched.</param>
/// <param name="Failures">Lines a rule matched but the posting refused, with the reason (the run carries on).</param>
public sealed record BankRuleRunResult(IReadOnlyList<BankRulePosting> Posted, IReadOnlyList<string> Failures);

/// <summary>Bank rules: maintain them and apply them to open statement lines (brief §4.7).</summary>
public interface IBankRuleService
{
    /// <summary>
    /// Creates a rule. Refuses a blank phrase or account, an unknown account or bank account, an inverted
    /// amount range, or a sub-ledger control account (AR / AP / stock / GRNI).
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rule id.</returns>
    Task<Result<long>> CreateRuleAsync(CreateBankRuleRequest request, CancellationToken ct = default);

    /// <summary>Activates or deactivates a rule.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ruleId">The rule.</param>
    /// <param name="isActive">Whether the rule should apply.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or not-found.</returns>
    Task<Result> SetActiveAsync(long companyId, long ruleId, bool isActive, CancellationToken ct = default);

    /// <summary>Lists a company's rules, in the order they are tried.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rules.</returns>
    Task<IReadOnlyList<BankRule>> ListAsync(long companyId, CancellationToken ct = default);

    /// <summary>
    /// Applies the active rules to every open statement line of a bank account dated on or before a date:
    /// each line a rule matches is posted to the rule's account and matched (through the statement-posting
    /// service, so each posting and its match commit together).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="bankAccountId">The bank account.</param>
    /// <param name="upToDate">Lines dated on or before this are considered.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was posted, or not-found for the bank account.</returns>
    Task<Result<BankRuleRunResult>> ApplyAsync(long companyId, long bankAccountId, DateOnly upToDate, CancellationToken ct = default);
}
