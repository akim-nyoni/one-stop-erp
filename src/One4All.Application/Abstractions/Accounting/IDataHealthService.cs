using One4All.Application.Accounting;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Accounting;

/// <summary>One indicator on the dashboard.</summary>
/// <param name="Kind">The indicator.</param>
/// <param name="Status">Its status.</param>
/// <param name="Count">What it found.</param>
/// <param name="Value">The money involved (base currency), where it applies.</param>
/// <param name="Items">The findings (at most 20), for drill-down.</param>
public sealed record HealthIndicator(HealthIndicatorKind Kind, HealthStatus Status, int Count, decimal Value, IReadOnlyList<string> Items);

/// <summary>The Data Health dashboard.</summary>
/// <param name="AsAtDate">The date control accounts were reconciled at.</param>
/// <param name="Overall">The worst indicator's status.</param>
/// <param name="Indicators">The indicators.</param>
public sealed record DataHealthReport(DateOnly AsAtDate, HealthStatus Overall, IReadOnlyList<HealthIndicator> Indicators);

/// <summary>
/// The Data Health dashboard (brief §4.17): orphan records, unbalanced batches, control account variances, negative stock
/// and unposted batches - with the rest of the ledger integrity check - in one graded view.
/// </summary>
public interface IDataHealthService
{
    /// <summary>Takes the company's data health.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The date to reconcile control accounts at.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The dashboard, or why it could not be taken.</returns>
    Task<Result<DataHealthReport>> SnapshotAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default);
}
