namespace One4All.Application.Accounting;

/// <summary>How healthy one area of the data is.</summary>
public enum HealthStatus
{
    /// <summary>Nothing found.</summary>
    Ok = 0,

    /// <summary>Worth attention (unposted work, orphans, numbering gaps).</summary>
    Warning = 1,

    /// <summary>The data disagrees with itself or with reality and must be corrected.</summary>
    Error = 2,
}

/// <summary>A data health indicator's kind (brief §4.17 Data Health dashboard).</summary>
public enum HealthIndicatorKind
{
    /// <summary>Posted journals whose debits and credits differ.</summary>
    UnbalancedBatches = 1,

    /// <summary>Control accounts that differ from their sub-ledgers.</summary>
    ControlVariances = 2,

    /// <summary>Stock balances below zero.</summary>
    NegativeStock = 3,

    /// <summary>Journals drafted, deferred or awaiting approval.</summary>
    UnpostedBatches = 4,

    /// <summary>Balances left on deleted customers, suppliers or items.</summary>
    OrphanRecords = 5,

    /// <summary>Other integrity checks: cached balances, stock history, open items, cost layers, numbering.</summary>
    LedgerIntegrity = 6,
}

/// <summary>
/// Grading of the Data Health dashboard (brief §4.17): an indicator with nothing found is OK; unbalanced batches, control
/// variances, negative stock and integrity errors are errors; unposted batches, orphans and integrity warnings are
/// warnings; and the dashboard is as bad as its worst indicator. Pure and unit-tested.
/// </summary>
public static class DataHealthGrading
{
    /// <summary>An indicator's status.</summary>
    /// <param name="kind">The indicator.</param>
    /// <param name="count">What it found.</param>
    /// <param name="hasErrors">For ledger integrity: whether any finding is an error rather than a warning.</param>
    /// <returns>The status.</returns>
    public static HealthStatus Grade(HealthIndicatorKind kind, int count, bool hasErrors = false) => count == 0
        ? HealthStatus.Ok
        : kind switch
        {
            HealthIndicatorKind.UnbalancedBatches or HealthIndicatorKind.ControlVariances or HealthIndicatorKind.NegativeStock => HealthStatus.Error,
            HealthIndicatorKind.LedgerIntegrity => hasErrors ? HealthStatus.Error : HealthStatus.Warning,
            _ => HealthStatus.Warning,
        };

    /// <summary>The dashboard's overall status: its worst indicator.</summary>
    /// <param name="statuses">The indicators' statuses.</param>
    /// <returns>The overall status.</returns>
    public static HealthStatus Overall(IEnumerable<HealthStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        return statuses.DefaultIfEmpty(HealthStatus.Ok).Max();
    }
}
