using One4All.Core.Common;

namespace One4All.Core.Accounting;

/// <summary>
/// Income billed in advance and earned evenly over a service period (<c>gl.deferred_income_schedule</c>; IFRS 15 over-time
/// recognition — school fees for a term, annual licences). The billing credits <see cref="DeferredAccountCode"/>; the
/// recognition run moves the earned share to <see cref="RevenueAccountCode"/> day by day.
/// </summary>
public sealed class DeferredIncomeSchedule : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private DeferredIncomeSchedule()
    {
        Reference = string.Empty;
        Description = string.Empty;
        DeferredAccountCode = string.Empty;
        RevenueAccountCode = string.Empty;
    }

    /// <summary>Creates a schedule.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="reference">The billing document it defers (e.g. the invoice number).</param>
    /// <param name="description">What the income is for.</param>
    /// <param name="deferredAccountCode">The deferred income (liability) account the billing credited.</param>
    /// <param name="revenueAccountCode">The revenue account it is earned into.</param>
    /// <param name="amount">The amount deferred (positive).</param>
    /// <param name="startDate">The first day of the service period.</param>
    /// <param name="endDate">The last day of the service period.</param>
    /// <exception cref="ArgumentException">Thrown for a non-positive amount or a period that ends before it starts.</exception>
    public DeferredIncomeSchedule(long companyId, string reference, string description, string deferredAccountCode, string revenueAccountCode, decimal amount, DateOnly startDate, DateOnly endDate)
    {
        if (amount <= 0m)
        {
            throw new ArgumentException("A deferred amount must be positive.", nameof(amount));
        }

        if (endDate < startDate)
        {
            throw new ArgumentException("The service period cannot end before it starts.", nameof(endDate));
        }

        CompanyId = companyId;
        Reference = reference;
        Description = description;
        DeferredAccountCode = deferredAccountCode;
        RevenueAccountCode = revenueAccountCode;
        Amount = amount;
        StartDate = startDate;
        EndDate = endDate;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The billing document it defers.</summary>
    public string Reference { get; private set; }

    /// <summary>What the income is for.</summary>
    public string Description { get; private set; }

    /// <summary>The deferred income account.</summary>
    public string DeferredAccountCode { get; private set; }

    /// <summary>The revenue account.</summary>
    public string RevenueAccountCode { get; private set; }

    /// <summary>The amount deferred.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The first day of the service period.</summary>
    public DateOnly StartDate { get; private set; }

    /// <summary>The last day of the service period.</summary>
    public DateOnly EndDate { get; private set; }

    /// <summary>How much has been recognised as revenue so far.</summary>
    public decimal RecognisedAmount { get; private set; }

    /// <summary>The share earned by the end of <paramref name="asAtDate"/>: the amount × days elapsed ÷ days in the period (all of it from the last day).</summary>
    /// <param name="asAtDate">The date.</param>
    /// <returns>The earned share, rounded to cents.</returns>
    public decimal EarnedBy(DateOnly asAtDate)
    {
        if (asAtDate < StartDate)
        {
            return 0m;
        }

        if (asAtDate >= EndDate)
        {
            return Amount;
        }

        int elapsed = asAtDate.DayNumber - StartDate.DayNumber + 1;
        int total = EndDate.DayNumber - StartDate.DayNumber + 1;
        return decimal.Round(Amount * elapsed / total, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Records revenue recognised (negative when a later run finds less earned, e.g. after a date correction).</summary>
    /// <param name="amount">The amount recognised by this run.</param>
    public void Recognise(decimal amount) => RecognisedAmount += amount;

    /// <summary>Cuts the schedule short (e.g. a student leaving): it now ends on the date, its amount reduced by what was credited back.</summary>
    /// <param name="endDate">The new last day.</param>
    /// <param name="credited">The amount credited back.</param>
    public void Curtail(DateOnly endDate, decimal credited)
    {
        Amount -= credited;
        EndDate = endDate < StartDate ? StartDate : endDate < EndDate ? endDate : EndDate;
    }
}
