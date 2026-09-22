using One4All.Core.Common;

namespace One4All.Application.Abstractions.Accounting;

/// <summary>One line of a recurring-journal template.</summary>
/// <param name="AccountCode">The account.</param>
/// <param name="IsDebit">True for a debit, false for a credit.</param>
/// <param name="Amount">The amount (positive).</param>
/// <param name="CostCentre">The cost centre, if any.</param>
/// <param name="Department">The department, if any.</param>
public readonly record struct RecurringJournalLineInput(string AccountCode, bool IsDebit, decimal Amount, string? CostCentre = null, string? Department = null);

/// <summary>
/// Manages recurring-journal templates: balanced sets of lines saved once and posted repeatedly (rent,
/// standing charges, provisions). Each post generates a real journal through the central posting engine.
/// </summary>
public interface IRecurringJournalService
{
    /// <summary>Creates a recurring-journal template. The lines must balance (debits equal credits).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The template code (unique per company).</param>
    /// <param name="description">The description.</param>
    /// <param name="sourceModule">The module the generated journals post under (defaults to GL).</param>
    /// <param name="lines">The template lines.</param>
    /// <param name="narrative">The narrative for the generated journals.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created template id.</returns>
    Task<Result<long>> CreateAsync(long companyId, string code, string description, string sourceModule, IReadOnlyList<RecurringJournalLineInput> lines, string? narrative = null, CancellationToken ct = default);

    /// <summary>
    /// Posts a recurring-journal template as a real journal on the given date, returning the journal number.
    /// Refuses an inactive or unbalanced template.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="recurringJournalId">The template.</param>
    /// <param name="date">The posting date.</param>
    /// <param name="documentNumber">An optional document number (defaults to the template code).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The generated journal number.</returns>
    Task<Result<string>> PostAsync(long companyId, long recurringJournalId, DateOnly date, string? documentNumber = null, CancellationToken ct = default);

    /// <summary>Activates or deactivates a template.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="recurringJournalId">The template.</param>
    /// <param name="isActive">Whether the template is active.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> SetActiveAsync(long companyId, long recurringJournalId, bool isActive, CancellationToken ct = default);
}
