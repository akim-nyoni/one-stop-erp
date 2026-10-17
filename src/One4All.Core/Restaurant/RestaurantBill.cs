using One4All.Core.Common;

namespace One4All.Core.Restaurant;

/// <summary>
/// A persisted record of a restaurant bill (<c>res.bill</c>) - the money breakdown captured each time a table
/// order (or a split of it) is billed, for an audit trail, reporting and reversal. One order may have several
/// bills (splits / per-seat / per-tender).
/// </summary>
public sealed class RestaurantBill : AuditableEntity, ICompanyScoped
{
    private readonly List<RestaurantBillPosting> _postings = [];
    private readonly List<RestaurantBillIssue> _issues = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private RestaurantBill() => RevenueJournalNumber = string.Empty;

    /// <summary>Records a bill.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="tableOrderId">The order billed.</param>
    /// <param name="date">The billing date.</param>
    /// <param name="net">The net (tax-exclusive) total.</param>
    /// <param name="tax">The tax total.</param>
    /// <param name="serviceCharge">The service charge / gratuity.</param>
    /// <param name="tip">The tip.</param>
    /// <param name="discount">The discount allowed.</param>
    /// <param name="gross">The gross charged.</param>
    /// <param name="costOfSales">The recipe cost of sales released.</param>
    /// <param name="revenueJournalNumber">The revenue GL journal number (empty when nothing was charged).</param>
    /// <param name="tenderReversible">True when the bill was settled by a tender we can reverse (not a house account).</param>
    public RestaurantBill(long companyId, long tableOrderId, DateOnly date, decimal net, decimal tax, decimal serviceCharge, decimal tip, decimal discount, decimal gross, decimal costOfSales, string revenueJournalNumber, bool tenderReversible)
    {
        CompanyId = companyId;
        TableOrderId = tableOrderId;
        Date = date;
        Net = net;
        Tax = tax;
        ServiceCharge = serviceCharge;
        Tip = tip;
        Discount = discount;
        Gross = gross;
        CostOfSales = costOfSales;
        RevenueJournalNumber = revenueJournalNumber;
        TenderReversible = tenderReversible;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The order billed.</summary>
    public long TableOrderId { get; private set; }

    /// <summary>The billing date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The net (tax-exclusive) total.</summary>
    public decimal Net { get; private set; }

    /// <summary>The tax total.</summary>
    public decimal Tax { get; private set; }

    /// <summary>The service charge / gratuity.</summary>
    public decimal ServiceCharge { get; private set; }

    /// <summary>The tip.</summary>
    public decimal Tip { get; private set; }

    /// <summary>The discount allowed.</summary>
    public decimal Discount { get; private set; }

    /// <summary>The gross charged.</summary>
    public decimal Gross { get; private set; }

    /// <summary>The recipe cost of sales released.</summary>
    public decimal CostOfSales { get; private set; }

    /// <summary>The revenue GL journal number (empty when nothing was charged).</summary>
    public string RevenueJournalNumber { get; private set; }

    /// <summary>True when the bill was settled by a tender we can reverse (false for a house-account/AR charge).</summary>
    public bool TenderReversible { get; private set; }

    /// <summary>True once the bill has been reversed.</summary>
    public bool IsReversed { get; private set; }

    /// <summary>The reversal date, if reversed.</summary>
    public DateOnly? ReversedOn { get; private set; }

    /// <summary>The reversal journal number, if reversed.</summary>
    public string? ReversalJournalNumber { get; private set; }

    /// <summary>The revenue journal lines posted (for exact reversal).</summary>
    public IReadOnlyList<RestaurantBillPosting> Postings => _postings.AsReadOnly();

    /// <summary>The stock issue movements released as cost of sales (for reversal).</summary>
    public IReadOnlyList<RestaurantBillIssue> Issues => _issues.AsReadOnly();

    /// <summary>Records a revenue journal line for reversal.</summary>
    /// <param name="accountCode">The account.</param>
    /// <param name="isDebit">True for a debit, false for a credit.</param>
    /// <param name="amount">The amount.</param>
    public void AddPosting(string accountCode, bool isDebit, decimal amount) => _postings.Add(new RestaurantBillPosting(accountCode, isDebit, amount));

    /// <summary>Records a stock issue movement for reversal.</summary>
    /// <param name="stockMovementId">The issue movement id.</param>
    public void AddIssue(long stockMovementId) => _issues.Add(new RestaurantBillIssue(stockMovementId));

    /// <summary>Marks the bill reversed.</summary>
    /// <param name="date">The reversal date.</param>
    /// <param name="reversalJournalNumber">The reversal journal number, if any.</param>
    public void MarkReversed(DateOnly date, string? reversalJournalNumber)
    {
        IsReversed = true;
        ReversedOn = date;
        ReversalJournalNumber = reversalJournalNumber;
    }
}

/// <summary>One revenue journal line of a bill (<c>res.bill_posting</c>), kept so the bill can be reversed exactly.</summary>
public sealed class RestaurantBillPosting : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private RestaurantBillPosting() => AccountCode = string.Empty;

    /// <summary>Creates a posting line.</summary>
    /// <param name="accountCode">The account.</param>
    /// <param name="isDebit">True for a debit, false for a credit.</param>
    /// <param name="amount">The amount.</param>
    public RestaurantBillPosting(string accountCode, bool isDebit, decimal amount)
    {
        AccountCode = accountCode;
        IsDebit = isDebit;
        Amount = amount;
    }

    /// <summary>The owning bill id.</summary>
    public long RestaurantBillId { get; private set; }

    /// <summary>The account.</summary>
    public string AccountCode { get; private set; }

    /// <summary>True for a debit, false for a credit.</summary>
    public bool IsDebit { get; private set; }

    /// <summary>The amount.</summary>
    public decimal Amount { get; private set; }
}

/// <summary>A stock issue released by a bill (<c>res.bill_issue</c>), kept so cost of sales can be reversed.</summary>
public sealed class RestaurantBillIssue : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private RestaurantBillIssue()
    {
    }

    /// <summary>Creates a bill-issue link.</summary>
    /// <param name="stockMovementId">The issue movement id.</param>
    public RestaurantBillIssue(long stockMovementId) => StockMovementId = stockMovementId;

    /// <summary>The owning bill id.</summary>
    public long RestaurantBillId { get; private set; }

    /// <summary>The issue movement id.</summary>
    public long StockMovementId { get; private set; }
}
