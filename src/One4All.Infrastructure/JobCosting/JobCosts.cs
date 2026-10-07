using Microsoft.EntityFrameworkCore;
using One4All.Core.JobCosting;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.JobCosting;

/// <summary>Captures a job cost and the overhead recovered on it at the company's rate for that kind of cost (brief §4.12).</summary>
internal static class JobCosts
{
    /// <summary>Captures the cost, plus its overhead when a rate is set for its source.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="contract">The job (tracked).</param>
    /// <param name="costHead">The cost head.</param>
    /// <param name="source">Where the cost came from.</param>
    /// <param name="amount">The amount (base).</param>
    /// <param name="date">The date.</param>
    /// <param name="reference">A reference.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The overhead added (0 when none).</returns>
    public static async Task<decimal> CaptureAsync(One4AllDbContext db, JobContract contract, string costHead, JobCostSource source, decimal amount, DateOnly date, string? reference,
        CancellationToken ct)
    {
        contract.CaptureCost(costHead, source, amount, date, reference);
        if (source == JobCostSource.Overhead || amount == 0m)
        {
            return 0m;
        }

        JobOverheadRate? rate = await db.Set<JobOverheadRate>().AsNoTracking().FirstOrDefaultAsync(r => r.CompanyId == contract.CompanyId && r.AppliesTo == source, ct).ConfigureAwait(false);
        if (rate is null || rate.Percent == 0m)
        {
            return 0m;
        }

        decimal overhead = decimal.Round(amount * rate.Percent / 100m, 2, MidpointRounding.AwayFromZero);
        contract.CaptureCost(rate.CostHead, JobCostSource.Overhead, overhead, date, FormattableString.Invariant($"{rate.Percent:0.##}% on {source} {reference}").Trim());
        return overhead;
    }
}
