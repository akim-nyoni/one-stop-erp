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
    private readonly List<LeaveSickTier> _sickTiers = [];

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

    /// <summary>The most the balance may ever hold (accrual stops there; the rest is forfeited); null: no ceiling.</summary>
    public decimal? MaxAccumulationDays { get; set; }

    /// <summary>Months into the new leave year that carried-over days stay usable (any not taken by then expire); null: they never expire.</summary>
    public int? CarryOverExpiryMonths { get; set; }

    /// <summary>How far below nil leave may be taken in advance (0: not at all).</summary>
    public decimal NegativeBalanceDays { get; set; }

    /// <summary>
    /// A rolling sick-leave cycle in months (36 for a three-year cycle): the sick days already taken are counted over that many months back from the
    /// period, not from the start of the leave year; null: the leave year.
    /// </summary>
    public int? SickCycleMonths { get; set; }

    /// <summary>Whether the leave type is unpaid leave: staff loan instalments are held back for a pay date it covers (interest still accrues).</summary>
    public bool IsUnpaid { get; set; }

    /// <summary>Sick pay tiers: from the nth day taken in the leave year, the share of pay (days before the first tier are on full pay).</summary>
    public IReadOnlyList<LeaveSickTier> SickTiers => _sickTiers.OrderBy(t => t.FromDay).ToList();

    /// <summary>Replaces the sick pay tiers.</summary>
    /// <param name="tiers">The tiers (from day → percentage of pay).</param>
    public void SetSickTiers(IEnumerable<(int FromDay, decimal PayPercent)> tiers)
    {
        _sickTiers.Clear();
        _sickTiers.AddRange(tiers.Select(t => new LeaveSickTier(t.FromDay, t.PayPercent)));
    }

    /// <summary>Whether the policy is in use.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Gets or sets the yearly days by grade ("B1=24; B2=28"), in place of <see cref="AnnualDays"/> for those grades (a service band still wins when higher).</summary>
    public string? GradeDays { get; set; }

    /// <summary>Gets or sets the most people of one department who may be away on any leave at once (null: no limit).</summary>
    public int? MaxAwayPerDepartment { get; set; }

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

/// <summary>A sick pay tier: from the nth sick day in the leave year, this share of pay.</summary>
public sealed class LeaveSickTier : Entity
{
    private LeaveSickTier()
    {
    }

    /// <summary>Creates the tier.</summary>
    /// <param name="fromDay">The first day (1-based) in the leave year it applies from.</param>
    /// <param name="payPercent">The percentage of pay for those days.</param>
    public LeaveSickTier(int fromDay, decimal payPercent)
    {
        FromDay = fromDay;
        PayPercent = payPercent;
    }

    /// <summary>The policy.</summary>
    public long LeavePolicyId { get; private set; }

    /// <summary>The first day (1-based) in the leave year it applies from.</summary>
    public int FromDay { get; private set; }

    /// <summary>The percentage of pay.</summary>
    public decimal PayPercent { get; private set; }
}
