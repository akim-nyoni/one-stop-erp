using One4All.Core.Common;

namespace One4All.Core.SalesOrders;

/// <summary>
/// A customer supply contract (<c>sop.customer_contract</c>, brief §4.13): agreed quantities of items at agreed prices for a period,
/// drawn down by call-off orders.
/// </summary>
public sealed class CustomerContract : AuditableEntity, ICompanyScoped
{
    private readonly List<CustomerContractLine> _lines = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private CustomerContract() => ContractNumber = string.Empty;

    /// <summary>Creates the contract.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="contractNumber">The contract number.</param>
    /// <param name="startDate">The first day call-offs may be made.</param>
    /// <param name="endDate">The last day.</param>
    public CustomerContract(long companyId, long customerId, string contractNumber, DateOnly startDate, DateOnly endDate)
    {
        CompanyId = companyId;
        CustomerId = customerId;
        ContractNumber = contractNumber.Trim().ToUpperInvariant();
        StartDate = startDate;
        EndDate = endDate;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The customer.</summary>
    public long CustomerId { get; private set; }

    /// <summary>The contract number.</summary>
    public string ContractNumber { get; private set; }

    /// <summary>The first day call-offs may be made.</summary>
    public DateOnly StartDate { get; private set; }

    /// <summary>The last day.</summary>
    public DateOnly EndDate { get; private set; }

    /// <summary>The lines.</summary>
    public IReadOnlyList<CustomerContractLine> Lines => _lines.AsReadOnly();

    /// <summary>Adds a line.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="agreedQuantity">The quantity agreed for the contract.</param>
    /// <param name="unitPrice">The agreed price.</param>
    public void AddLine(long itemId, decimal agreedQuantity, decimal unitPrice) => _lines.Add(new CustomerContractLine(itemId, agreedQuantity, unitPrice));
}

/// <summary>One item on a customer contract.</summary>
public sealed class CustomerContractLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private CustomerContractLine()
    {
    }

    /// <summary>Creates the line.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="agreedQuantity">The quantity agreed.</param>
    /// <param name="unitPrice">The agreed price.</param>
    public CustomerContractLine(long itemId, decimal agreedQuantity, decimal unitPrice)
    {
        ItemId = itemId;
        AgreedQuantity = agreedQuantity;
        UnitPrice = unitPrice;
    }

    /// <summary>The owning contract.</summary>
    public long CustomerContractId { get; private set; }

    /// <summary>The item.</summary>
    public long ItemId { get; private set; }

    /// <summary>The quantity agreed.</summary>
    public decimal AgreedQuantity { get; private set; }

    /// <summary>The agreed price.</summary>
    public decimal UnitPrice { get; private set; }

    /// <summary>The quantity called off so far.</summary>
    public decimal CalledOffQuantity { get; private set; }

    /// <summary>Gets the quantity still to be called off.</summary>
    public decimal Remaining => AgreedQuantity - CalledOffQuantity;

    /// <summary>Records a call-off.</summary>
    /// <param name="quantity">The quantity called off.</param>
    public void CallOff(decimal quantity) => CalledOffQuantity += quantity;
}
