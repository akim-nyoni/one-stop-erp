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

    /// <summary>The fee items.</summary>
    public IReadOnlyList<FeeItem> Items => _items.AsReadOnly();

    /// <summary>The total of the compulsory items — the default term charge before discounts.</summary>
    public decimal CompulsoryTotal => _items.Where(i => i.IsCompulsory).Sum(i => i.Amount);

    /// <summary>Adds a fee item.</summary>
    /// <param name="description">The item description.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="isCompulsory">Whether the item is compulsory.</param>
    /// <returns>The created item.</returns>
    public FeeItem AddItem(string description, decimal amount, bool isCompulsory)
    {
        FeeItem item = new(description, amount, isCompulsory);
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
}
