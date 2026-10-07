using One4All.Core.Common;

namespace One4All.Core.System;

/// <summary>The posting status of a period for a module (brief §3.4).</summary>
public enum PeriodStatus
{
    /// <summary>Future period — posting blocked unless the company allows future posting.</summary>
    Future = 0,

    /// <summary>Open — posting permitted.</summary>
    Open = 1,

    /// <summary>Soft-closed — posting blocked, adjustments allowed with permission.</summary>
    Closed = 2,

    /// <summary>Hard-closed — no posting ever again.</summary>
    PermanentlyClosed = 3,
}

/// <summary>
/// A fiscal year for a company (<c>sys.fiscal_year</c>), divided into <see cref="Periods"/>. Supports
/// 12/13/4-4-5 configurations via the number and dates of the periods created.
/// </summary>
public sealed class FiscalYear : AuditableEntity, ICompanyScoped
{
    private readonly List<FiscalPeriod> _periods = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private FiscalYear() => Code = string.Empty;

    /// <summary>Creates a fiscal year.</summary>
    /// <param name="companyId">The owning company.</param>
    /// <param name="code">A label such as "2026" or "FY2026/27".</param>
    /// <param name="startDate">First day of the year.</param>
    /// <param name="endDate">Last day of the year.</param>
    public FiscalYear(long companyId, string code, DateOnly startDate, DateOnly endDate)
    {
        CompanyId = companyId;
        Code = code;
        StartDate = startDate;
        EndDate = endDate;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The year label.</summary>
    public string Code { get; private set; }

    /// <summary>First day of the year.</summary>
    public DateOnly StartDate { get; private set; }

    /// <summary>Last day of the year.</summary>
    public DateOnly EndDate { get; private set; }

    /// <summary>True once the year has been formally closed (year-end run committed).</summary>
    public bool IsClosed { get; private set; }

    /// <summary>The periods in this year (persisted navigation).</summary>
    public IReadOnlyCollection<FiscalPeriod> Periods => _periods.AsReadOnly();

    /// <summary>The periods ordered by number, for display and iteration.</summary>
    public IEnumerable<FiscalPeriod> OrderedPeriods => _periods.OrderBy(p => p.PeriodNumber);

    /// <summary>Adds a period to the year.</summary>
    /// <param name="periodNumber">1-based period number.</param>
    /// <param name="name">Period name (e.g. "January").</param>
    /// <param name="start">First day of the period.</param>
    /// <param name="end">Last day of the period.</param>
    /// <returns>The created period.</returns>
    public FiscalPeriod AddPeriod(int periodNumber, string name, DateOnly start, DateOnly end)
    {
        FiscalPeriod period = new(periodNumber, name, start, end);
        _periods.Add(period);
        return period;
    }

    /// <summary>Marks the year closed.</summary>
    public void Close() => IsClosed = true;

    /// <summary>Whether the year-end is final: its periods are permanently closed and the year-end can no longer be reversed.</summary>
    public bool IsHardLocked { get; private set; }

    /// <summary>Reopens a closed year whose year-end was reversed (not once hard-locked).</summary>
    /// <returns>Success, or why not.</returns>
    public Result Reopen()
    {
        if (IsHardLocked)
        {
            return Result.Failure(Error.Rule("YearEnd.HardLocked", $"Fiscal year {Code} is hard-locked; its year-end cannot be reversed."));
        }

        IsClosed = false;
        return Result.Success();
    }

    /// <summary>Makes the year-end final.</summary>
    public void HardLock() => IsHardLocked = true;

    /// <summary>Adds the adjustment ("13th") period: the year's last day, reached only by journals that ask for it (audit and year-end adjustments).</summary>
    /// <returns>The period.</returns>
    public FiscalPeriod AddAdjustmentPeriod()
    {
        FiscalPeriod period = new(_periods.Count == 0 ? 1 : _periods.Max(p => p.PeriodNumber) + 1, "Adjustments " + Code, EndDate, EndDate) { IsAdjustment = true };
        _periods.Add(period);
        return period;
    }

    /// <summary>The adjustment period, if the year has one.</summary>
    public FiscalPeriod? AdjustmentPeriod => _periods.FirstOrDefault(p => p.IsAdjustment);
}
