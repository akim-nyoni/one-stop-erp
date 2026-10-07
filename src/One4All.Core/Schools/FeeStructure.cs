using One4All.Core.Common;

namespace One4All.Core.Schools;

/// <summary>
/// A fee structure for a grade/term (brief §4.11, <c>sch.fee_structure</c>). Groups the compulsory and
/// optional fee items billed to a student for a term; the compulsory items form the default term charge.
/// </summary>
public sealed class FeeStructure : SoftDeletableEntity, ICompanyScoped
{
    private readonly List<FeeItem> _items = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private FeeStructure()
    {
        Code = string.Empty;
        Name = string.Empty;
        Grade = string.Empty;
        Term = string.Empty;
        RevenueAccountCode = string.Empty;
    }

    /// <summary>Creates a fee structure.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The structure code.</param>
    /// <param name="name">The structure name.</param>
    /// <param name="grade">The grade/form/class it applies to.</param>
    /// <param name="term">The term/semester.</param>
    /// <param name="revenueAccountCode">The fee-income account credited.</param>
    public FeeStructure(long companyId, string code, string name, string grade, string term, string revenueAccountCode)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        Grade = grade;
        Term = term;
        RevenueAccountCode = revenueAccountCode;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The structure code.</summary>
    public string Code { get; private set; }

    /// <summary>The structure name.</summary>
    public string Name { get; set; }

    /// <summary>The grade/form/class.</summary>
    public string Grade { get; private set; }

    /// <summary>The term/semester.</summary>
    public string Term { get; private set; }

    /// <summary>The fee-income account credited.</summary>
    public string RevenueAccountCode { get; private set; }

    /// <summary>The first day of the term the fees pay for, when fees are deferred over it.</summary>
    public DateOnly? TermStart { get; private set; }

    /// <summary>The last day of the term, when fees are deferred over it.</summary>
    public DateOnly? TermEnd { get; private set; }

    /// <summary>
    /// The deferred income (liability) account term fees are billed to when they are earned over the term (IFRS 15), or
    /// null when they are recognised as revenue on billing.
    /// </summary>
    public string? DeferredIncomeAccountCode { get; private set; }

    /// <summary>Whether billed fees are deferred and earned over the term.</summary>
    public bool DefersIncome => DeferredIncomeAccountCode is not null && TermStart is not null && TermEnd is not null;

    /// <summary>Bills this structure's fees to deferred income and earns them evenly over the term.</summary>
    /// <param name="termStart">The first day of the term.</param>
    /// <param name="termEnd">The last day of the term.</param>
    /// <param name="deferredIncomeAccountCode">The deferred income account.</param>
    /// <exception cref="ArgumentException">Thrown when the term ends before it starts or the account is blank.</exception>
    public void DeferOverTerm(DateOnly termStart, DateOnly termEnd, string deferredIncomeAccountCode)
    {
        if (termEnd < termStart)
        {
            throw new ArgumentException("The term cannot end before it starts.", nameof(termEnd));
        }

        if (string.IsNullOrWhiteSpace(deferredIncomeAccountCode))
        {
            throw new ArgumentException("A deferred income account is required.", nameof(deferredIncomeAccountCode));
        }

        TermStart = termStart;
        TermEnd = termEnd;
        DeferredIncomeAccountCode = deferredIncomeAccountCode.Trim();
    }

    /// <summary>The fee items.</summary>
    /// <summary>Whether a student who joins or leaves during the term pays only for the days enrolled (needs the term dates).</summary>
    public bool ProRata { get; private set; }

    /// <summary>Sets the term dates and whether joiners and leavers pay pro rata.</summary>
    /// <param name="termStart">The first day of term.</param>
    /// <param name="termEnd">The last day of term.</param>
    /// <param name="proRata">Whether to pro-rate.</param>
    public void SetTerm(DateOnly termStart, DateOnly termEnd, bool proRata)
    {
        if (termEnd < termStart)
        {
            throw new ArgumentException("The term cannot end before it starts.", nameof(termEnd));
        }

        TermStart = termStart;
        TermEnd = termEnd;
        ProRata = proRata;
    }

    /// <summary>The share of the term a student enrolled from one date to another pays (1 when not pro rata or the term dates are not set).</summary>
    /// <param name="from">Enrolled from (null: the whole term).</param>
    /// <param name="to">Enrolled to (null: the whole term).</param>
    /// <returns>The share, 0–1, to four places.</returns>
    public decimal ShareOfTerm(DateOnly? from, DateOnly? to)
    {
        if (!ProRata || TermStart is not DateOnly start || TermEnd is not DateOnly end)
        {
            return 1m;
        }

        DateOnly first = from is DateOnly f && f > start ? f : start, last = to is DateOnly t && t < end ? t : end;
        if (last < first)
        {
            return 0m;
        }

        return decimal.Round((decimal)(last.DayNumber - first.DayNumber + 1) / (end.DayNumber - start.DayNumber + 1), 4, MidpointRounding.AwayFromZero);
    }

    public IReadOnlyList<FeeItem> Items => _items.AsReadOnly();

    /// <summary>The total of the compulsory items — the default term charge before discounts.</summary>
    public decimal CompulsoryTotal => _items.Where(i => i.IsCompulsory).Sum(i => i.Amount);

    /// <summary>Adds a fee item.</summary>
    /// <param name="description">The item description.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="isCompulsory">Whether the item is compulsory.</param>
    /// <returns>The created item.</returns>
    public FeeItem AddItem(string description, decimal amount, bool isCompulsory, string? revenueAccountCode = null)
    {
        FeeItem item = new(description, amount, isCompulsory) { RevenueAccountCode = string.IsNullOrWhiteSpace(revenueAccountCode) ? null : revenueAccountCode.Trim() };
        _items.Add(item);
        return item;
    }
}

/// <summary>One line of a fee structure (<c>sch.fee_item</c>).</summary>
public sealed class FeeItem : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private FeeItem() => Description = string.Empty;

    /// <summary>Creates a fee item.</summary>
    /// <param name="description">The description.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="isCompulsory">Whether the item is compulsory.</param>
    public FeeItem(string description, decimal amount, bool isCompulsory)
    {
        Description = description;
        Amount = amount;
        IsCompulsory = isCompulsory;
    }

    /// <summary>The owning fee-structure id.</summary>
    public long FeeStructureId { get; private set; }

    /// <summary>The description (tuition, levy, boarding, transport, exam, trip …).</summary>
    public string Description { get; private set; }

    /// <summary>The amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Whether the item is compulsory (billed by default) or optional (opt-in).</summary>
    public bool IsCompulsory { get; private set; }

    /// <summary>The revenue account for an optional item (the structure's when blank).</summary>
    public string? RevenueAccountCode { get; set; }
}

/// <summary>A student taking an optional fee item (lunches, a club, a trip), billed with the term (<c>sch.fee_option</c>, brief §4.11).</summary>
public sealed class StudentFeeOption : Entity, ICompanyScoped
{
    private StudentFeeOption()
    {
    }

    /// <summary>Records the choice.</summary>
    /// <param name="companyId">The school.</param>
    /// <param name="studentId">The student.</param>
    /// <param name="feeItemId">The optional item.</param>
    public StudentFeeOption(long companyId, long studentId, long feeItemId)
    {
        CompanyId = companyId;
        StudentId = studentId;
        FeeItemId = feeItemId;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The student.</summary>
    public long StudentId { get; private set; }

    /// <summary>The optional item.</summary>
    public long FeeItemId { get; private set; }
}
