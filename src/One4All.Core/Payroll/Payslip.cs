using One4All.Core.Common;

namespace One4All.Core.Payroll;

/// <summary>What kind of run produced a payslip (brief §4.15.6: corrections after commit are visible runs, never edits).</summary>
public enum PayRunKind
{
    /// <summary>The period's regular run.</summary>
    Regular = 0,

    /// <summary>A supplementary / adjustment run correcting a committed period.</summary>
    Supplementary = 1,

    /// <summary>A formally approved reversal of a committed run (negative payslips).</summary>
    Reversal = 2,

    /// <summary>Backpay for a retrospective change, paid in a later period.</summary>
    Backpay = 3,
}

/// <summary>
/// A payslip produced by a payroll run (brief §4.15.6, <c>pay.payslip</c>). Immutable once the run is
/// committed; holds the resolved earning/deduction lines and the gross/deduction/net totals.
/// </summary>
public sealed class Payslip : AuditableEntity, ICompanyScoped
{
    private readonly List<PayslipLine> _lines = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Payslip() => PeriodLabel = string.Empty;

    /// <summary>Creates a payslip.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="periodLabel">The pay period label.</param>
    /// <param name="payDate">The pay date.</param>
    /// <param name="kind">The kind of run.</param>
    /// <param name="runReference">The run's document number (links the payslip to its run).</param>
    /// <param name="payGroup">The pay group the run was for ("" for the default payroll).</param>
    public Payslip(long companyId, long employeeId, string periodLabel, DateOnly payDate, PayRunKind kind = PayRunKind.Regular, string runReference = "", string payGroup = "")
    {
        PayGroup = payGroup;
        CompanyId = companyId;
        EmployeeId = employeeId;
        PeriodLabel = periodLabel;
        PayDate = payDate;
        RunKind = kind;
        RunReference = runReference;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>The pay period label.</summary>
    public string PeriodLabel { get; private set; }

    /// <summary>The pay date.</summary>
    public DateOnly PayDate { get; private set; }

    /// <summary>The kind of run that produced it.</summary>
    public PayRunKind RunKind { get; private set; }

    /// <summary>The run's document number (blank for runs made before runs were referenced).</summary>
    public string RunReference { get; private set; } = string.Empty;

    /// <summary>The pay group the run was for ("" for the default payroll).</summary>
    public string PayGroup { get; private set; } = string.Empty;

    /// <summary>The gross earnings.</summary>
    public decimal GrossEarnings { get; private set; }

    /// <summary>The total deductions.</summary>
    public decimal TotalDeductions { get; private set; }

    /// <summary>The net pay.</summary>
    public decimal NetPay { get; private set; }

    /// <summary>The resolved lines.</summary>
    public IReadOnlyList<PayslipLine> Lines => _lines.AsReadOnly();

    /// <summary>Adds a resolved line and updates the totals.</summary>
    /// <param name="elementCode">The element code.</param>
    /// <param name="description">The description.</param>
    /// <param name="type">The element type.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="taxable">Whether the line is taxable.</param>
    public void AddLine(string elementCode, string description, PayElementType type, decimal amount, bool taxable)
    {
        _lines.Add(new PayslipLine(elementCode, description, type, amount, taxable));
        switch (type)
        {
            case PayElementType.Earning:
                GrossEarnings += amount;
                break;
            case PayElementType.Deduction:
                TotalDeductions += amount;
                break;
        }

        NetPay = GrossEarnings - TotalDeductions;
    }
}

/// <summary>One resolved line of a payslip (<c>pay.payslip_line</c>).</summary>
public sealed class PayslipLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PayslipLine()
    {
        ElementCode = string.Empty;
        Description = string.Empty;
    }

    /// <summary>Creates a payslip line.</summary>
    /// <param name="elementCode">The element code.</param>
    /// <param name="description">The description.</param>
    /// <param name="type">The element type.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="taxable">Whether the line is taxable.</param>
    public PayslipLine(string elementCode, string description, PayElementType type, decimal amount, bool taxable)
    {
        ElementCode = elementCode;
        Description = description;
        Type = type;
        Amount = amount;
        Taxable = taxable;
    }

    /// <summary>The owning payslip id.</summary>
    public long PayslipId { get; private set; }

    /// <summary>The element code.</summary>
    public string ElementCode { get; private set; }

    /// <summary>The description.</summary>
    public string Description { get; private set; }

    /// <summary>The element type.</summary>
    public PayElementType Type { get; private set; }

    /// <summary>The amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Whether the line is taxable.</summary>
    public bool Taxable { get; private set; }
}
