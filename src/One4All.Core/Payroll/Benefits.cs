using One4All.Core.Common;

namespace One4All.Core.Payroll;

/// <summary>A taxable benefit in kind (brief §4.15.4).</summary>
public enum BenefitKind
{
    /// <summary>A company car, valued from the vehicle scale by engine capacity or cost (or a fixed value).</summary>
    MotorVehicle = 1,

    /// <summary>Housing provided.</summary>
    Housing = 2,

    /// <summary>School fees paid for the employee's children.</summary>
    SchoolFees = 3,

    /// <summary>Medical cover paid by the employer.</summary>
    Medical = 4,

    /// <summary>Any other benefit.</summary>
    Other = 5,
}

/// <summary>What a vehicle scale band is measured on.</summary>
public enum VehicleScaleBasis
{
    /// <summary>Engine capacity in cc.</summary>
    EngineCapacity = 1,

    /// <summary>The vehicle's cost.</summary>
    Cost = 2,
}

/// <summary>A benefit an employee enjoys for a time; taxed each period it applies, shown on the payslip as a memo line.</summary>
public sealed class EmployeeBenefit : AuditableEntity, ICompanyScoped
{
    private EmployeeBenefit()
    {
        Description = string.Empty;
    }

    /// <summary>Records a benefit.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="description">What it is (a registration number, a property).</param>
    /// <param name="from">When it starts.</param>
    public EmployeeBenefit(long companyId, long employeeId, BenefitKind kind, string description, DateOnly from)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        Kind = kind;
        Description = description.Trim();
        From = from;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>Gets the kind.</summary>
    public BenefitKind Kind { get; private set; }

    /// <summary>Gets what it is.</summary>
    public string Description { get; private set; }

    /// <summary>Gets or sets a vehicle's engine capacity (cc), for a scale measured on it.</summary>
    public int? EngineCapacityCc { get; set; }

    /// <summary>Gets or sets a vehicle's cost, for a scale measured on it.</summary>
    public decimal? Cost { get; set; }

    /// <summary>Gets or sets the monthly taxable value when it is not taken from a scale.</summary>
    public decimal? MonthlyValue { get; set; }

    /// <summary>Gets when it starts.</summary>
    public DateOnly From { get; private set; }

    /// <summary>Gets or sets when it ends (null: open).</summary>
    public DateOnly? To { get; set; }

    /// <summary>Whether it applies on a date.</summary>
    /// <param name="date">The date.</param>
    /// <returns>True when in force.</returns>
    public bool AppliesOn(DateOnly date) => From <= date && (To is null || date <= To);
}

/// <summary>A band of the motor vehicle benefit scale: the monthly taxable value of a car whose capacity or cost falls in it, from a date.</summary>
public sealed class VehicleBenefitScale : AuditableEntity, ICompanyScoped
{
    private VehicleBenefitScale()
    {
    }

    /// <summary>Creates a band.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="basis">What the band is measured on.</param>
    /// <param name="from">The lower bound (inclusive).</param>
    /// <param name="to">The upper bound (inclusive; null: open).</param>
    /// <param name="monthlyValue">The monthly taxable value.</param>
    /// <param name="effectiveFrom">When the band applies from.</param>
    public VehicleBenefitScale(long companyId, VehicleScaleBasis basis, decimal from, decimal? to, decimal monthlyValue, DateOnly effectiveFrom)
    {
        CompanyId = companyId;
        Basis = basis;
        From = from;
        To = to;
        MonthlyValue = monthlyValue;
        EffectiveFrom = effectiveFrom;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets what the band is measured on.</summary>
    public VehicleScaleBasis Basis { get; private set; }

    /// <summary>Gets the lower bound.</summary>
    public decimal From { get; private set; }

    /// <summary>Gets the upper bound (null: open).</summary>
    public decimal? To { get; private set; }

    /// <summary>Gets the monthly taxable value.</summary>
    public decimal MonthlyValue { get; private set; }

    /// <summary>Gets when the band applies from.</summary>
    public DateOnly EffectiveFrom { get; private set; }
}

/// <summary>Values benefits in kind for a month (pure).</summary>
public static class BenefitValuation
{
    /// <summary>A benefit's monthly taxable value on a date: its own value, else the vehicle scale in force (by capacity or cost), else nothing.</summary>
    /// <param name="benefit">The benefit.</param>
    /// <param name="scales">The vehicle scale bands.</param>
    /// <param name="date">The date.</param>
    /// <returns>The monthly value.</returns>
    public static decimal Monthly(EmployeeBenefit benefit, IEnumerable<VehicleBenefitScale> scales, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(benefit);
        ArgumentNullException.ThrowIfNull(scales);
        if (!benefit.AppliesOn(date))
        {
            return 0m;
        }

        if (benefit.MonthlyValue is decimal fixedValue || benefit.Kind != BenefitKind.MotorVehicle)
        {
            return benefit.MonthlyValue ?? 0m;
        }

        (VehicleScaleBasis basis, decimal? measure) = benefit.EngineCapacityCc is int cc ? (VehicleScaleBasis.EngineCapacity, cc) : (VehicleScaleBasis.Cost, benefit.Cost);
        if (measure is not decimal value)
        {
            return 0m;
        }

        List<VehicleBenefitScale> inForce = [.. scales.Where(s => s.Basis == basis && s.EffectiveFrom <= date)];
        DateOnly? latest = inForce.Count == 0 ? null : inForce.Max(s => s.EffectiveFrom);
        return inForce.Where(s => s.EffectiveFrom == latest && s.From <= value && (s.To is null || value <= s.To)).Select(s => s.MonthlyValue).FirstOrDefault();
    }

    /// <summary>The month's taxable benefit of a cheap loan: the outstanding balance at the deemed rate less the rate the loan charges.</summary>
    /// <param name="outstanding">The balance owed.</param>
    /// <param name="loanRatePercent">The annual rate the loan charges.</param>
    /// <param name="deemedRatePercent">The annual deemed (official) rate.</param>
    /// <returns>The monthly benefit.</returns>
    public static decimal LoanBenefit(decimal outstanding, decimal loanRatePercent, decimal deemedRatePercent)
        => outstanding <= 0m ? 0m : decimal.Round(outstanding * Math.Max(0m, deemedRatePercent - loanRatePercent) / 100m / 12m, 2, MidpointRounding.AwayFromZero);
}
