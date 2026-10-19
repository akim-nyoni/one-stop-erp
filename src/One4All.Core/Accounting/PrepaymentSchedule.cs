using One4All.Core.Common;

namespace One4All.Core.Accounting;

/// <summary>
/// A prepayment released to expense over the months it covers (<c>gl.prepayment_schedule</c>, brief §3.4): an annual insurance premium or
/// licence paid in advance sits in the prepayment account and is charged a month at a time. Equal monthly amounts, the last month taking
/// the rounding.
/// </summary>
public sealed class PrepaymentSchedule : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PrepaymentSchedule()
    {
        Reference = string.Empty;
        Description = string.Empty;
        PrepaymentAccountCode = string.Empty;
        ExpenseAccountCode = string.Empty;
    }

    /// <summary>Creates a schedule.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="reference">The reference (unique per company).</param>
    /// <param name="description">What was prepaid.</param>
    /// <param name="prepaymentAccountCode">The prepayment (asset) account released from.</param>
    /// <param name="expenseAccountCode">The expense account charged.</param>
    /// <param name="total">The amount to release.</param>
    /// <param name="firstMonth">The first month covered (any day in it).</param>
    /// <param name="months">How many months it covers.</param>
    public PrepaymentSchedule(long companyId, string reference, string description, string prepaymentAccountCode, string expenseAccountCode, decimal total, DateOnly firstMonth, int months)
    {
        CompanyId = companyId;
        Reference = reference.Trim().ToUpperInvariant();
        Description = description.Trim();
        PrepaymentAccountCode = prepaymentAccountCode.Trim();
        ExpenseAccountCode = expenseAccountCode.Trim();
        Total = total;
        FirstMonth = new DateOnly(firstMonth.Year, firstMonth.Month, 1);
        Months = months;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets the reference.</summary>
    public string Reference { get; private set; }

    /// <summary>Gets what was prepaid.</summary>
    public string Description { get; private set; }

    /// <summary>Gets the prepayment account released from.</summary>
    public string PrepaymentAccountCode { get; private set; }

    /// <summary>Gets the expense account charged.</summary>
    public string ExpenseAccountCode { get; private set; }

    /// <summary>Gets or sets the cost centre the expense is charged to.</summary>
    public string? CostCentre { get; set; }

    /// <summary>Gets or sets the department the expense is charged to.</summary>
    public string? Department { get; set; }

    /// <summary>Gets the amount to release.</summary>
    public decimal Total { get; private set; }

    /// <summary>Gets the first month covered (its first day).</summary>
    public DateOnly FirstMonth { get; private set; }

    /// <summary>Gets how many months it covers.</summary>
    public int Months { get; private set; }

    /// <summary>Gets how many months have been released.</summary>
    public int ReleasedMonths { get; private set; }

    /// <summary>Gets the amount released so far.</summary>
    public decimal Released { get; private set; }

    /// <summary>Gets a value indicating whether everything has been released.</summary>
    public bool IsFinished => ReleasedMonths >= Months;

    /// <summary>The last day of a month of the schedule (0 is the first month).</summary>
    /// <param name="index">The month.</param>
    /// <returns>Its last day.</returns>
    public DateOnly MonthEnd(int index) => FirstMonth.AddMonths(index + 1).AddDays(-1);

    /// <summary>The amount a month releases: an equal share, the last month taking what is left.</summary>
    /// <param name="index">The month (0 is the first).</param>
    /// <returns>The amount.</returns>
    public decimal AmountFor(int index)
    {
        decimal share = decimal.Round(Total / Months, 2, MidpointRounding.AwayFromZero);
        return index < Months - 1 ? share : Total - (share * (Months - 1));
    }

    /// <summary>Records the next month as released.</summary>
    /// <param name="amount">What it released.</param>
    public void MarkReleased(decimal amount)
    {
        ReleasedMonths++;
        Released += amount;
    }
}
