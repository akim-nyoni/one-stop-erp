using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Accounting;

/// <summary>One line of a recurring-journal template.</summary>
/// <param name="AccountCode">The account.</param>
/// <param name="IsDebit">True for a debit, false for a credit.</param>
/// <param name="Amount">The amount (positive).</param>
/// <param name="CostCentre">The cost centre, if any.</param>
/// <param name="Department">The department, if any.</param>
public readonly record struct RecurringJournalLineInput(string AccountCode, bool IsDebit, decimal Amount, string? CostCentre = null, string? Department = null);

/// <summary>The result of posting one template in a recurring-journal run.</summary>
/// <param name="Code">The template code.</param>
/// <param name="JournalNumber">The generated journal number (empty when the template was skipped).</param>
/// <param name="AlreadyPosted">True when the template had already been posted for the run's month and was skipped.</param>
public readonly record struct RecurringRunLine(string Code, string JournalNumber, bool AlreadyPosted = false, string? Note = null, string? ReversalJournalNumber = null);

/// <summary>A recurring journal's schedule (brief §3.1): how often, between which dates, how many times, auto-reversal and currency.</summary>
/// <param name="IntervalMonths">Months between postings: 1 monthly, 3 quarterly, 6 half-yearly, 12 yearly.</param>
/// <param name="StartDate">The first month it may post in, or null for any.</param>
/// <param name="EndDate">The last date it may post on, or null for no end.</param>
/// <param name="MaxOccurrences">How many times it may post in all, or null for no limit.</param>
/// <param name="AutoReverse">Whether each posting is reversed on the first day of the following month (accruals).</param>
/// <param name="Currency">The currency of the amounts; null is the company's base currency.</param>
public sealed record RecurringSchedule(int IntervalMonths = 1, DateOnly? StartDate = null, DateOnly? EndDate = null, int? MaxOccurrences = null, bool AutoReverse = false, CurrencyCode? Currency = null);

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

    /// <summary>Creates a template with a schedule (see <see cref="CreateAsync(long, string, string, string, IReadOnlyList{RecurringJournalLineInput}, string?, CancellationToken)"/>).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The template code.</param>
    /// <param name="description">The description.</param>
    /// <param name="sourceModule">The source module.</param>
    /// <param name="lines">The lines.</param>
    /// <param name="schedule">The schedule.</param>
    /// <param name="narrative">The narrative.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The template id, or an error.</returns>
    Task<Result<long>> CreateAsync(long companyId, string code, string description, string sourceModule, IReadOnlyList<RecurringJournalLineInput> lines, RecurringSchedule schedule, string? narrative = null, CancellationToken ct = default);

    /// <summary>Changes a template's schedule; postings already made stand.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="recurringJournalId">The template.</param>
    /// <param name="schedule">The schedule.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or an error.</returns>
    Task<Result> SetScheduleAsync(long companyId, long recurringJournalId, RecurringSchedule schedule, CancellationToken ct = default);

    /// <summary>
    /// Posts a recurring-journal template as a real journal on the given date, returning the journal number.
    /// Refuses an inactive or unbalanced template, and one already posted for that month or a later one
    /// (<c>Gl.RecurringAlreadyPosted</c>) - posting is forward-only, one journal per template per month.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="recurringJournalId">The template.</param>
    /// <param name="date">The posting date.</param>
    /// <param name="documentNumber">An optional document number (defaults to the template code).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The generated journal number.</returns>
    Task<Result<string>> PostAsync(long companyId, long recurringJournalId, DateOnly date, string? documentNumber = null, CancellationToken ct = default);

    /// <summary>
    /// Posts every active, balanced recurring template for the company on the given date (a month-end run).
    /// Templates already posted for the month are skipped (reported with <see cref="RecurringRunLine.AlreadyPosted"/>),
    /// so the run can be repeated safely. Stops and returns the error if any template fails to post; templates posted
    /// before the failure stand, and a re-run after fixing the cause posts only the remainder.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="date">The posting date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One result line per active template, posted or skipped.</returns>
    Task<Result<IReadOnlyList<RecurringRunLine>>> PostAllActiveAsync(long companyId, DateOnly date, CancellationToken ct = default);

    /// <summary>Activates or deactivates a template.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="recurringJournalId">The template.</param>
    /// <param name="isActive">Whether the template is active.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> SetActiveAsync(long companyId, long recurringJournalId, bool isActive, CancellationToken ct = default);
}
