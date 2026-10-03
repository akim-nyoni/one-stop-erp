using One4All.Core.Common;

namespace One4All.Core.Accounting;

/// <summary>
/// A saved recurring-journal template (<c>gl.recurring_journal</c>): a balanced set of lines that can be
/// posted repeatedly (rent, standing charges, provisions) — each post generates a real journal through the
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
    public void RecordPosted(DateOnly date) => LastPostedPeriod = PeriodOf(date);

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
