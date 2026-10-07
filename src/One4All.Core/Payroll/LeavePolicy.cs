using One4All.Core.Common;

namespace One4All.Core.Payroll;

/// <summary>
/// A leave policy for one leave type (<c>pay.leave_policy</c>, brief §4.15): the yearly entitlement (rising with service in bands), earned
/// monthly or granted at the start of the leave year, pro rata for joiners, and how much may be carried into the next leave year (the
/// rest is forfeited when the year turns).
/// </summary>
public sealed class LeavePolicy : AuditableEntity, ICompanyScoped
{
    private readonly List<LeaveServiceBand> _bands = [];

    private LeavePolicy() => LeaveType = string.Empty;

    /// <summary>Creates the policy.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="leaveType">The leave type it governs.</param>
    /// <param name="annualDays">The yearly entitlement before service bands.</param>
    public LeavePolicy(long companyId, string leaveType, decimal annualDays)
    {
        CompanyId = companyId;
        LeaveType = leaveType.Trim().ToUpperInvariant();
        AnnualDays = annualDays;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The leave type.</summary>
    public string LeaveType { get; private set; }

    /// <summary>The yearly entitlement before service bands.</summary>
    public decimal AnnualDays { get; set; }

    /// <summary>Earned month by month (a twelfth a month) rather than granted at the start of the leave year.</summary>
    public bool AccruesMonthly { get; set; } = true;

    /// <summary>The month the leave year starts (1–12).</summary>
    public int LeaveYearStartMonth { get; set; } = 1;

    /// <summary>The most that may be carried into a new leave year (null: no cap).</summary>
    public decimal? CarryOverCapDays { get; set; }

    /// <summary>Whether a joiner earns only for the days employed in the first month (monthly) or the months left (yearly grant).</summary>
    public bool ProRataJoiners { get; set; } = true;

    /// <summary>Whether the policy is in use.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Entitlements by completed years of service (the highest band reached applies).</summary>
    public IReadOnlyList<LeaveServiceBand> Bands => _bands.OrderBy(b => b.FromYears).ToList();

    /// <summary>Replaces the service bands.</summary>
    /// <param name="bands">The bands (completed years → yearly days).</param>
    public void SetBands(IEnumerable<(int FromYears, decimal AnnualDays)> bands)
    {
        _bands.Clear();
        _bands.AddRange(bands.Select(b => new LeaveServiceBand(b.FromYears, b.AnnualDays)));
    }
}

/// <summary>A service band: from so many completed years, so many days a year.</summary>
public sealed class LeaveServiceBand : Entity
{
    private LeaveServiceBand()
    {
    }

    /// <summary>Creates the band.</summary>
    /// <param name="fromYears">Completed years of service.</param>
    /// <param name="annualDays">Days a year from then.</param>
    public LeaveServiceBand(int fromYears, decimal annualDays)
    {
        FromYears = fromYears;
        AnnualDays = annualDays;
    }

    /// <summary>The policy.</summary>
    public long LeavePolicyId { get; private set; }

    /// <summary>Completed years of service.</summary>
    public int FromYears { get; private set; }

    /// <summary>Days a year from then.</summary>
    public decimal AnnualDays { get; private set; }
}
