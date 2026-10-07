using One4All.Core.Common;

namespace One4All.Core.Payroll;

/// <summary>A carried-out termination with its final pay (brief §4.15.8).</summary>
public sealed class Termination : AuditableEntity, ICompanyScoped
{
    private readonly List<TerminationClearanceItem> _clearance = [];

    private Termination() => Reason = string.Empty;

    /// <summary>Records a termination.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The leaver.</param>
    /// <param name="type">Why (the termination type, stored as its number).</param>
    /// <param name="lastDay">The last day of service.</param>
    /// <param name="reason">The reason given.</param>
    public Termination(long companyId, long employeeId, int type, DateOnly lastDay, string reason)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        Type = type;
        LastDay = lastDay;
        Reason = reason;
    }

    /// <summary>The company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>The leaver.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>The termination type.</summary>
    public int Type { get; private set; }

    /// <summary>The last day of service.</summary>
    public DateOnly LastDay { get; private set; }

    /// <summary>The reason given.</summary>
    public string Reason { get; private set; }

    /// <summary>Who authorised it.</summary>
    public long ApprovedByUserId { get; set; }

    /// <summary>The final pay journal.</summary>
    public string? JournalNumber { get; set; }

    /// <summary>Completed years of service.</summary>
    public int YearsOfService { get; set; }

    /// <summary>Last month's pro-rata salary.</summary>
    public decimal ProRataSalary { get; set; }

    /// <summary>Notice pay.</summary>
    public decimal Notice { get; set; }

    /// <summary>Leave days paid out.</summary>
    public decimal LeaveDays { get; set; }

    /// <summary>Leave pay.</summary>
    public decimal Leave { get; set; }

    /// <summary>Gratuity.</summary>
    public decimal Gratuity { get; set; }

    /// <summary>Severance.</summary>
    public decimal Severance { get; set; }

    /// <summary>PAYE on ordinary pay.</summary>
    public decimal OrdinaryTax { get; set; }

    /// <summary>Tax on terminal benefits.</summary>
    public decimal TerminalTax { get; set; }

    /// <summary>Loans recovered.</summary>
    public decimal Recoveries { get; set; }

    /// <summary>Loans still owed after the final pay.</summary>
    public decimal UnrecoveredLoans { get; set; }

    /// <summary>The net final pay.</summary>
    public decimal NetPay { get; set; }

    /// <summary>The clearance checklist.</summary>
    public IReadOnlyList<TerminationClearanceItem> Clearance => _clearance.AsReadOnly();

    /// <summary>Adds a clearance item.</summary>
    /// <param name="description">What must be cleared.</param>
    /// <param name="cleared">Whether it already is.</param>
    public void AddClearance(string description, bool cleared) => _clearance.Add(new TerminationClearanceItem(description, cleared));
}

/// <summary>One item a leaver must clear (assets returned, loans settled, housing vacated...).</summary>
public sealed class TerminationClearanceItem : Entity
{
    private TerminationClearanceItem() => Description = string.Empty;

    /// <summary>Creates the item.</summary>
    /// <param name="description">What must be cleared.</param>
    /// <param name="cleared">Whether it already is.</param>
    public TerminationClearanceItem(string description, bool cleared)
    {
        Description = description;
        IsCleared = cleared;
    }

    /// <summary>The termination.</summary>
    public long TerminationId { get; private set; }

    /// <summary>What must be cleared.</summary>
    public string Description { get; private set; }

    /// <summary>Whether it is cleared.</summary>
    public bool IsCleared { get; private set; }

    /// <summary>Who cleared it.</summary>
    public long? ClearedByUserId { get; private set; }

    /// <summary>When.</summary>
    public DateOnly? ClearedOn { get; private set; }

    /// <summary>Ticks it off.</summary>
    /// <param name="userId">Who.</param>
    /// <param name="date">When.</param>
    public void Clear(long userId, DateOnly date)
    {
        IsCleared = true;
        ClearedByUserId = userId;
        ClearedOn = date;
    }
}
