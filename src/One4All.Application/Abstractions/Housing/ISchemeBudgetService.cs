using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A professional fee or overhead to capitalise to a scheme.</summary>
public sealed record SchemeCostRequest
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The scheme code.</summary>
    public string SchemeCode { get; init; } = string.Empty;

    /// <summary>Professional fees or overheads.</summary>
    public SchemeCostElement Element { get; init; }

    /// <summary>When.</summary>
    public DateOnly Date { get; init; }

    /// <summary>The amount.</summary>
    public decimal Amount { get; init; }

    /// <summary>The invoice or voucher.</summary>
    public string Reference { get; init; } = string.Empty;

    /// <summary>What it was.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The stands account the cost is capitalised to.</summary>
    public string StandsAccountCode { get; init; } = string.Empty;

    /// <summary>The bank or payable account it was funded from.</summary>
    public string FundingAccountCode { get; init; } = string.Empty;
}

/// <summary>
/// Scheme budgets and capitalised scheme costs (brief §14 H84): an approved budget per cost element, and professional
/// fees and overheads capitalised into the scheme's unsold stands (equal shares) so they are recovered on sale.
/// </summary>
public interface ISchemeBudgetService
{
    /// <summary>Sets (or replaces) a scheme's budget for a cost element.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="schemeCode">The scheme.</param>
    /// <param name="element">The element.</param>
    /// <param name="amount">The budget.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why not.</returns>
    Task<Result> SetBudgetAsync(long companyId, string schemeCode, SchemeCostElement element, decimal amount, CancellationToken ct = default);

    /// <summary>Capitalises a professional fee or overhead to a scheme's unsold stands.</summary>
    /// <param name="request">The cost.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The journal number.</returns>
    Task<Result<string>> RecordCostAsync(SchemeCostRequest request, CancellationToken ct = default);
}
