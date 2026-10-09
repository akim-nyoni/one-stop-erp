using One4All.Core.Common;

namespace One4All.Core.Banking;

/// <summary>
/// A flow keyed into the cash forecast by hand (<c>cb.cash_forecast_entry</c>, brief §4.4): a loan drawdown, a tax payment, a grant
/// expected — once, or repeating monthly until a date. Positive comes in, negative goes out.
/// </summary>
public sealed class CashForecastEntry : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private CashForecastEntry()
    {
        Description = string.Empty;
    }

    /// <summary>Creates an entry.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="date">The (first) date.</param>
    /// <param name="amount">The amount: positive in, negative out.</param>
    /// <param name="description">What it is.</param>
    public CashForecastEntry(long companyId, DateOnly date, decimal amount, string description)
    {
        CompanyId = companyId;
        Date = date;
        Amount = amount;
        Description = description;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets the (first) date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets the amount: positive in, negative out.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Gets what it is.</summary>
    public string Description { get; private set; }

    /// <summary>Gets or sets the last date a monthly repeat falls (null: once only).</summary>
    public DateOnly? RepeatMonthlyUntil { get; set; }

    /// <summary>The dates it falls on.</summary>
    /// <returns>The dates.</returns>
    public IEnumerable<DateOnly> Dates()
    {
        DateOnly date = Date;
        int months = 0;
        do
        {
            yield return date;
            date = Date.AddMonths(++months);
        }
        while (RepeatMonthlyUntil is DateOnly until && date <= until);
    }
}
