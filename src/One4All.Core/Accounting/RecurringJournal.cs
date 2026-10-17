using One4All.Core.Common;

namespace One4All.Core.Accounting;

/// <summary>
/// A saved recurring-journal template (<c>gl.recurring_journal</c>): a balanced set of lines that can be
/// posted repeatedly (rent, standing charges, provisions) - each post generates a real journal through the
/// central posting engine. The template itself never touches the ledger.
/// </summary>
public sealed class RecurringJournal : AuditableEntity, ICompanyScoped
{
    private readonly List<RecurringJournalLine> _lines = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private RecurringJournal()
    {
        Code = string.Empty;
        Description = string.Empty;
        SourceModule = string.Empty;
    }

    /// <summary>Creates a recurring-journal template.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The template code.</param>
    /// <param name="description">The description.</param>
    /// <param name="sourceModule">The module the generated journals post under.</param>
    /// <param name="narrative">The narrative for the generated journals.</param>
    public RecurringJournal(long companyId, string code, string description, string sourceModule, string? narrative = null)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Description = description;
        SourceModule = string.IsNullOrWhiteSpace(sourceModule) ? "GL" : sourceModule.Trim().ToUpperInvariant();
        Narrative = narrative;
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The template code.</summary>
    public string Code { get; private set; }

    /// <summary>The description.</summary>
    public string Description { get; set; }

    /// <summary>The module the generated journals post under.</summary>
    public string SourceModule { get; private set; }

    /// <summary>The narrative for the generated journals.</summary>
    public string? Narrative { get; set; }

    /// <summary>Whether the template is active.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Months between postings: 1 monthly, 3 quarterly, 6 half-yearly, 12 yearly.</summary>
    public int IntervalMonths { get; private set; } = 1;

    /// <summary>The first month it may post in, when limited.</summary>
    public DateOnly? StartDate { get; private set; }

    /// <summary>The last date it may post on, when limited.</summary>
    public DateOnly? EndDate { get; private set; }

    /// <summary>How many times it may post in all, when limited.</summary>
    public int? MaxOccurrences { get; private set; }

    /// <summary>How many times it has posted.</summary>
    public int Occurrences { get; private set; }

    /// <summary>Whether each posting is reversed on the first day of the following month (an accrual template).</summary>
    public bool AutoReverse { get; private set; }

    /// <summary>The currency the amounts are in; null is the company's base currency.</summary>
    public CurrencyCode? Currency { get; private set; }

    /// <summary>Sets the schedule: how often, between which dates, how many times, whether to auto-reverse, and in which currency.</summary>
    /// <param name="intervalMonths">Months between postings (1, 3, 6 or 12).</param>
    /// <param name="startDate">The first month it may post in, or null for any.</param>
    /// <param name="endDate">The last date it may post on, or null for no end.</param>
    /// <param name="maxOccurrences">How many times it may post in all, or null for no limit.</param>
    /// <param name="autoReverse">Whether each posting is reversed on the first day of the next month.</param>
    /// <param name="currency">The currency of the amounts, or null for base.</param>
    public void Schedule(int intervalMonths, DateOnly? startDate, DateOnly? endDate, int? maxOccurrences, bool autoReverse, CurrencyCode? currency)
    {
        IntervalMonths = intervalMonths;
        StartDate = startDate;
        EndDate = endDate;
        MaxOccurrences = maxOccurrences;
        AutoReverse = autoReverse;
        Currency = currency;
    }

    /// <summary>Why the template may not post on a date, or null when it may (ignoring whether the month is already posted).</summary>
    /// <param name="date">The posting date.</param>
    /// <returns>The reason, or null.</returns>
    public string? NotDueReason(DateOnly date)
    {
        if (StartDate is DateOnly start && PeriodOf(date) < PeriodOf(start))
        {
            return $"starts {start:yyyy-MM}";
        }

        if (EndDate is DateOnly end && date > end)
        {
            return $"ended {end:yyyy-MM-dd}";
        }

        if (MaxOccurrences is int max && Occurrences >= max)
        {
            return $"posted {Occurrences} of {max} times";
        }

        if (IntervalMonths > 1)
        {
            DateOnly anchor = StartDate ?? new DateOnly(date.Year, 1, 1);
            int months = ((date.Year - anchor.Year) * 12) + date.Month - anchor.Month;
            if (months < 0 || months % IntervalMonths != 0)
            {
                return $"every {IntervalMonths} months from {anchor:yyyy-MM}";
            }
        }

        return null;
    }

    /// <summary>Whether the template is due to post on the date: active, within its schedule and not yet posted for the month.</summary>
    /// <param name="date">The posting date.</param>
    /// <returns>True when due.</returns>
    public bool IsDueOn(DateOnly date) => IsActive && !IsPostedFor(date) && NotDueReason(date) is null;

    /// <summary>The template lines.</summary>
    public IReadOnlyList<RecurringJournalLine> Lines => _lines.AsReadOnly();

    /// <summary>Adds a line to the template.</summary>
    /// <param name="accountCode">The account.</param>
    /// <param name="isDebit">True for a debit, false for a credit.</param>
    /// <param name="amount">The amount (positive).</param>
    /// <param name="costCentre">The cost centre, if any.</param>
    /// <param name="department">The department, if any.</param>
    public void AddLine(string accountCode, bool isDebit, decimal amount, string? costCentre = null, string? department = null)
        => _lines.Add(new RecurringJournalLine(accountCode, isDebit, amount, costCentre, department));

    /// <summary>The total of the debit lines.</summary>
    public decimal TotalDebit => _lines.Where(l => l.IsDebit).Sum(l => l.Amount);

    /// <summary>The total of the credit lines.</summary>
    public decimal TotalCredit => _lines.Where(l => !l.IsDebit).Sum(l => l.Amount);

    /// <summary>Whether the template is balanced (debits equal credits) and has lines.</summary>
    public bool IsBalanced => _lines.Count > 0 && TotalDebit == TotalCredit;

    /// <summary>
    /// The latest month (<c>yyyyMM</c>) this template has been posted for, or null if never. Posting is forward-only:
    /// a month at or before this one is refused, so re-running a month's batch cannot post the charge twice.
    /// </summary>
    public int? LastPostedPeriod { get; private set; }

    /// <summary>The <c>yyyyMM</c> period key of a date.</summary>
    /// <param name="date">The date.</param>
    /// <returns>The period key.</returns>
    public static int PeriodOf(DateOnly date) => (date.Year * 100) + date.Month;

    /// <summary>Whether the template has already been posted for the month of <paramref name="date"/> (or a later one).</summary>
    /// <param name="date">Any date in the month.</param>
    /// <returns>True when a post for that month would be a repeat.</returns>
    public bool IsPostedFor(DateOnly date) => LastPostedPeriod is { } last && last >= PeriodOf(date);

    /// <summary>Records that the template was posted for the month of <paramref name="date"/>.</summary>
    /// <param name="date">The posting date.</param>
    public void RecordPosted(DateOnly date)
    {
        LastPostedPeriod = PeriodOf(date);
        Occurrences++;
    }

    /// <summary>Activates or deactivates the template.</summary>
    /// <param name="isActive">Whether the template is active.</param>
    public void SetActive(bool isActive) => IsActive = isActive;
}

/// <summary>One line of a recurring-journal template (<c>gl.recurring_journal_line</c>).</summary>
public sealed class RecurringJournalLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private RecurringJournalLine() => AccountCode = string.Empty;

    /// <summary>Creates a template line.</summary>
    /// <param name="accountCode">The account.</param>
    /// <param name="isDebit">True for a debit, false for a credit.</param>
    /// <param name="amount">The amount (positive).</param>
    /// <param name="costCentre">The cost centre, if any.</param>
    /// <param name="department">The department, if any.</param>
    public RecurringJournalLine(string accountCode, bool isDebit, decimal amount, string? costCentre = null, string? department = null)
    {
        AccountCode = accountCode;
        IsDebit = isDebit;
        Amount = amount;
        CostCentre = costCentre;
        Department = department;
    }

    /// <summary>The owning template id.</summary>
    public long RecurringJournalId { get; private set; }

    /// <summary>The account.</summary>
    public string AccountCode { get; private set; }

    /// <summary>True for a debit, false for a credit.</summary>
    public bool IsDebit { get; private set; }

    /// <summary>The amount (positive).</summary>
    public decimal Amount { get; private set; }

    /// <summary>The cost centre, if any.</summary>
    public string? CostCentre { get; private set; }

    /// <summary>The department, if any.</summary>
    public string? Department { get; private set; }
}
