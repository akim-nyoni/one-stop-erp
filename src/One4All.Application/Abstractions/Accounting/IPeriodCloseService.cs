namespace One4All.Application.Abstractions.Accounting;

/// <summary>One readiness check on the period-close checklist.</summary>
/// <param name="Code">A stable code, e.g. <c>UnpostedJournals</c>.</param>
/// <param name="Name">The check as shown to the controller.</param>
/// <param name="Count">How many items are outstanding (0 = passes).</param>
/// <param name="Value">The money value involved, where meaningful (0 otherwise).</param>
/// <param name="IsBlocking">True when outstanding items should stop the close; false for warnings.</param>
/// <param name="Detail">A one-line explanation of what was counted.</param>
public sealed record PeriodCloseCheck(string Code, string Name, int Count, decimal Value, bool IsBlocking, string Detail)
{
    /// <summary>True when nothing is outstanding.</summary>
    public bool Passes => Count == 0;
}

/// <summary>The period-close checklist for a company as at a period end.</summary>
/// <param name="PeriodEnd">The period-end date the checks are measured at.</param>
/// <param name="Checks">The checks, in the order a controller works through them.</param>
public sealed record PeriodCloseChecklist(DateOnly PeriodEnd, IReadOnlyList<PeriodCloseCheck> Checks)
{
    /// <summary>True when no blocking check has outstanding items.</summary>
    public bool IsReadyToClose => Checks.All(c => c.Passes || !c.IsBlocking);

    /// <summary>The number of checks with outstanding items.</summary>
    public int Outstanding => Checks.Count(c => !c.Passes);
}

/// <summary>
/// The period-end close checklist (brief §3.3): a cross-module readiness report a controller runs before
/// closing a period — unposted journals, unsynced offline sales, unreconciled statement lines, open
/// stocktakes, goods received not invoiced and approvals still in progress. Pure read model.
/// </summary>
public interface IPeriodCloseService
{
    /// <summary>Runs every check as at a period end.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="periodEnd">The period-end date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The checklist.</returns>
    Task<PeriodCloseChecklist> ChecklistAsync(long companyId, DateOnly periodEnd, CancellationToken ct = default);
}
