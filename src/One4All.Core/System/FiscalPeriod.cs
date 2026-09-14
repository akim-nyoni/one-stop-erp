using One4All.Core.Common;

namespace One4All.Core.System;

/// <summary>
/// A period within a <see cref="FiscalYear"/> (<c>sys.fiscal_period</c>). Each period holds a
/// <em>per-module</em> status so, for example, GL can be open while POS is closed (brief §3.4). The
/// posting engine consults <see cref="StatusFor"/> before allowing a posting.
/// </summary>
public sealed class FiscalPeriod : Entity
{
    private readonly List<PeriodModuleStatus> _moduleStatuses = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private FiscalPeriod() => Name = string.Empty;

    /// <summary>Creates a period.</summary>
    /// <param name="periodNumber">1-based period number within the year.</param>
    /// <param name="name">Period name.</param>
    /// <param name="start">First day of the period.</param>
    /// <param name="end">Last day of the period.</param>
    public FiscalPeriod(int periodNumber, string name, DateOnly start, DateOnly end)
    {
        PeriodNumber = periodNumber;
        Name = name;
        StartDate = start;
        EndDate = end;
    }

    /// <summary>The owning fiscal-year id.</summary>
    public long FiscalYearId { get; private set; }

    /// <summary>1-based period number within the year.</summary>
    public int PeriodNumber { get; private set; }

    /// <summary>Period name.</summary>
    public string Name { get; private set; }

    /// <summary>First day of the period.</summary>
    public DateOnly StartDate { get; private set; }

    /// <summary>Last day of the period.</summary>
    public DateOnly EndDate { get; private set; }

    /// <summary>Per-module posting statuses.</summary>
    public IReadOnlyCollection<PeriodModuleStatus> ModuleStatuses => _moduleStatuses.AsReadOnly();

    /// <summary>True when <paramref name="date"/> falls within this period.</summary>
    /// <param name="date">The accounting date.</param>
    /// <returns>True when in range.</returns>
    public bool Contains(DateOnly date) => date >= StartDate && date <= EndDate;

    /// <summary>Returns the status of this period for a module, defaulting to <see cref="PeriodStatus.Future"/>.</summary>
    /// <param name="moduleCode">The posting module code.</param>
    /// <returns>The module's period status.</returns>
    public PeriodStatus StatusFor(string moduleCode) =>
        _moduleStatuses.FirstOrDefault(s => string.Equals(s.ModuleCode, moduleCode, StringComparison.OrdinalIgnoreCase))?.Status
            ?? PeriodStatus.Future;

    /// <summary>
    /// Sets the status of this period for a module, enforcing the one-way lifecycle
    /// Future → Open → Closed → PermanentlyClosed (a permanently-closed period can never reopen).
    /// </summary>
    /// <param name="moduleCode">The posting module code.</param>
    /// <param name="status">The new status.</param>
    /// <returns>Success, or a rule error if the transition is illegal.</returns>
    public Result SetStatus(string moduleCode, PeriodStatus status)
    {
        PeriodModuleStatus? existing = _moduleStatuses.FirstOrDefault(s =>
            string.Equals(s.ModuleCode, moduleCode, StringComparison.OrdinalIgnoreCase));

        if (existing is not null && existing.Status == PeriodStatus.PermanentlyClosed)
        {
            return Result.Failure(Error.Rule(
                "Period.PermanentlyClosed",
                $"Period {PeriodNumber} for module {moduleCode} is permanently closed and cannot be reopened."));
        }

        if (existing is null)
        {
            _moduleStatuses.Add(new PeriodModuleStatus(moduleCode, status));
        }
        else
        {
            existing.SetStatus(status);
        }

        return Result.Success();
    }

    /// <summary>Whether posting is allowed for a module in this period (status is Open).</summary>
    /// <param name="moduleCode">The posting module code.</param>
    /// <returns>True when open.</returns>
    public bool IsOpenFor(string moduleCode) => StatusFor(moduleCode) == PeriodStatus.Open;
}

/// <summary>The status of a period for a specific posting module (<c>sys.period_module_status</c>).</summary>
public sealed class PeriodModuleStatus : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PeriodModuleStatus() => ModuleCode = string.Empty;

    /// <summary>Creates a per-module status.</summary>
    /// <param name="moduleCode">The posting module code.</param>
    /// <param name="status">The status.</param>
    public PeriodModuleStatus(string moduleCode, PeriodStatus status)
    {
        ModuleCode = moduleCode;
        Status = status;
    }

    /// <summary>The owning period id.</summary>
    public long FiscalPeriodId { get; private set; }

    /// <summary>The posting module code.</summary>
    public string ModuleCode { get; private set; }

    /// <summary>The status.</summary>
    public PeriodStatus Status { get; private set; }

    /// <summary>Replaces the status.</summary>
    /// <param name="status">The new status.</param>
    public void SetStatus(PeriodStatus status) => Status = status;
}
