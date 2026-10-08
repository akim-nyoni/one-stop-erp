using One4All.Core.Common;

namespace One4All.Core.Payroll;

/// <summary>One notch of a grade's salary scale (brief §4.15.1): the monthly salary at that notch, from a date (a new scale supersedes the old).</summary>
public sealed class SalaryScale : Entity, ICompanyScoped
{
    private SalaryScale()
    {
        Grade = string.Empty;
    }

    /// <summary>Creates a scale notch.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="grade">The grade.</param>
    /// <param name="notch">The notch (1 is the bottom).</param>
    /// <param name="monthlyAmount">The monthly salary.</param>
    /// <param name="effectiveFrom">When it applies from.</param>
    public SalaryScale(long companyId, string grade, int notch, decimal monthlyAmount, DateOnly effectiveFrom)
    {
        CompanyId = companyId;
        Grade = grade.Trim().ToUpperInvariant();
        Notch = notch;
        MonthlyAmount = monthlyAmount;
        EffectiveFrom = effectiveFrom;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the grade.</summary>
    public string Grade { get; private set; }

    /// <summary>Gets the notch.</summary>
    public int Notch { get; private set; }

    /// <summary>Gets or sets the monthly salary.</summary>
    public decimal MonthlyAmount { get; set; }

    /// <summary>Gets when it applies from.</summary>
    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>The salary for a grade and notch on a date: the latest scale in force for that grade, at that notch.</summary>
    /// <param name="scales">The company's scales.</param>
    /// <param name="grade">The grade.</param>
    /// <param name="notch">The notch.</param>
    /// <param name="date">The date.</param>
    /// <returns>The monthly salary, or null when the scale has no such notch.</returns>
    public static decimal? Amount(IEnumerable<SalaryScale> scales, string? grade, int? notch, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(scales);
        if (grade is null || notch is null)
        {
            return null;
        }

        string key = grade.Trim().ToUpperInvariant();
        List<SalaryScale> inForce = [.. scales.Where(s => s.Grade == key && s.EffectiveFrom <= date)];
        if (inForce.Count == 0)
        {
            return null;
        }

        DateOnly latest = inForce.Max(s => s.EffectiveFrom);
        return inForce.Where(s => s.EffectiveFrom == latest && s.Notch == notch).Select(s => (decimal?)s.MonthlyAmount).FirstOrDefault();
    }

    /// <summary>The top notch of a grade's scale in force on a date.</summary>
    /// <param name="scales">The company's scales.</param>
    /// <param name="grade">The grade.</param>
    /// <param name="date">The date.</param>
    /// <returns>The top notch, or null when the grade has no scale.</returns>
    public static int? TopNotch(IEnumerable<SalaryScale> scales, string? grade, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(scales);
        string key = (grade ?? string.Empty).Trim().ToUpperInvariant();
        List<SalaryScale> inForce = [.. scales.Where(s => s.Grade == key && s.EffectiveFrom <= date)];
        if (inForce.Count == 0)
        {
            return null;
        }

        DateOnly latest = inForce.Max(s => s.EffectiveFrom);
        return inForce.Where(s => s.EffectiveFrom == latest).Max(s => s.Notch);
    }
}

/// <summary>An employee acting in a higher post for a time: paid the difference between that post's scale and their own basic (brief §4.15.1).</summary>
public sealed class ActingAppointment : Entity, ICompanyScoped
{
    private ActingAppointment()
    {
        Grade = string.Empty;
    }

    /// <summary>Records an acting appointment.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="grade">The grade acted in.</param>
    /// <param name="notch">The notch of that grade.</param>
    /// <param name="from">The first day.</param>
    /// <param name="to">The last day (null: until ended).</param>
    public ActingAppointment(long companyId, long employeeId, string grade, int notch, DateOnly from, DateOnly? to)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        Grade = grade.Trim().ToUpperInvariant();
        Notch = notch;
        From = from;
        To = to;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>Gets the grade acted in.</summary>
    public string Grade { get; private set; }

    /// <summary>Gets the notch.</summary>
    public int Notch { get; private set; }

    /// <summary>Gets the first day.</summary>
    public DateOnly From { get; private set; }

    /// <summary>Gets or sets the last day.</summary>
    public DateOnly? To { get; set; }

    /// <summary>Whether the appointment covers a date.</summary>
    /// <param name="date">The date.</param>
    /// <returns>True when acting on it.</returns>
    public bool Covers(DateOnly date) => From <= date && (To is null || date <= To);
}
