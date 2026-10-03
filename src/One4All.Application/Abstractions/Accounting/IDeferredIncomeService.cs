using One4All.Core.Common;

namespace One4All.Application.Abstractions.Accounting;

/// <summary>One schedule's share of a recognition run.</summary>
/// <param name="Reference">The billing document deferred.</param>
/// <param name="Description">What the income is for.</param>
/// <param name="Amount">The amount deferred.</param>
/// <param name="EarnedToDate">Earned by the run date.</param>
/// <param name="RecognisedThisRun">Moved to revenue by this run.</param>
public readonly record struct DeferredIncomeLine(string Reference, string Description, decimal Amount, decimal EarnedToDate, decimal RecognisedThisRun);

/// <summary>The result of a recognition run.</summary>
/// <param name="AsAtDate">The run date.</param>
/// <param name="Lines">The schedules that recognised something.</param>
/// <param name="JournalNumber">The recognition journal, or null when nothing was due.</param>
public sealed record DeferredIncomeRunResult(DateOnly AsAtDate, IReadOnlyList<DeferredIncomeLine> Lines, string? JournalNumber)
{
    /// <summary>The total recognised by the run.</summary>
    public decimal Recognised => Lines.Sum(l => l.RecognisedThisRun);
}

/// <summary>
/// Recognises income billed in advance over its service period (brief §3.7, IFRS 15 over time): each
/// <c>DeferredIncomeSchedule</c> earns its amount evenly by day, and a run moves what has been earned by the date from
/// deferred income to revenue — only what is not yet recognised, so repeating a run posts nothing.
/// </summary>
public interface IDeferredIncomeService
{
    /// <summary>Recognises everything earned by a date.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The run date (normally a period end).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The run result.</returns>
    Task<Result<DeferredIncomeRunResult>> RecogniseAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default);
}
