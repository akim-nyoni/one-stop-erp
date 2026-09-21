using One4All.Core.Common;

namespace One4All.Core.Restaurant;

/// <summary>
/// A persisted record of a restaurant bill (<c>res.bill</c>) — the money breakdown captured each time a table
/// order (or a split of it) is billed, for an audit trail and reporting. One order may have several bills
/// (splits / per-seat / per-tender).
/// </summary>
public sealed class RestaurantBill : AuditableEntity, ICompanyScoped
{
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
    public RestaurantBill(long companyId, long tableOrderId, DateOnly date, decimal net, decimal tax, decimal serviceCharge, decimal tip, decimal discount, decimal gross, decimal costOfSales, string revenueJournalNumber)
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
}
