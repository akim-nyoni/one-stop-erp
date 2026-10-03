using One4All.Core.Common;

namespace One4All.Core.Inventory;

/// <summary>Where a store requisition stands.</summary>
public enum StoreRequisitionStatus
{
    /// <summary>Raised and awaiting approval.</summary>
    Requested = 0,

    /// <summary>Approved and waiting for the store to issue.</summary>
    Approved = 1,

    /// <summary>Partly issued.</summary>
    PartIssued = 2,

    /// <summary>Fully issued.</summary>
    Issued = 3,

    /// <summary>Rejected.</summary>
    Rejected = 4,
}

/// <summary>
/// An internal store requisition voucher (<c>inv.store_requisition</c>, brief §4.8.4): a department asks the store for
/// goods for its own use; once a different user approves, the store issues against it — in part or in full — charging
/// the department's expense account and cost centre, each issue under its own issue note number.
/// </summary>
public sealed class StoreRequisition : AuditableEntity, ICompanyScoped
{
    private readonly List<StoreRequisitionLine> _lines = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private StoreRequisition()
    {
        Number = string.Empty;
        ChargeAccountCode = string.Empty;
    }

    /// <summary>Raises a requisition.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="number">The requisition number.</param>
    /// <param name="date">When it was raised.</param>
    /// <param name="warehouseId">The store asked.</param>
    /// <param name="chargeAccountCode">The department's expense account the goods are charged to.</param>
    /// <param name="costCentre">The department's cost centre, if any.</param>
    /// <param name="requestedByUserId">Who asked.</param>
    public StoreRequisition(long companyId, string number, DateOnly date, long warehouseId, string chargeAccountCode, string? costCentre, long requestedByUserId)
    {
        CompanyId = companyId;
        Number = number;
        Date = date;
        WarehouseId = warehouseId;
        ChargeAccountCode = chargeAccountCode;
        CostCentre = costCentre;
        RequestedByUserId = requestedByUserId;
        Status = StoreRequisitionStatus.Requested;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The requisition number.</summary>
    public string Number { get; private set; }

    /// <summary>When it was raised.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The store asked.</summary>
    public long WarehouseId { get; private set; }

    /// <summary>The expense account the goods are charged to.</summary>
    public string ChargeAccountCode { get; private set; }

    /// <summary>The cost centre, if any.</summary>
    public string? CostCentre { get; private set; }

    /// <summary>Who asked.</summary>
    public long RequestedByUserId { get; private set; }

    /// <summary>Who approved it.</summary>
    public long? ApprovedByUserId { get; private set; }

    /// <summary>Where it stands.</summary>
    public StoreRequisitionStatus Status { get; private set; }

    /// <summary>The goods asked for.</summary>
    public IReadOnlyList<StoreRequisitionLine> Lines => _lines.AsReadOnly();

    /// <summary>Adds goods asked for.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">The quantity.</param>
    public void AddLine(long itemId, decimal quantity) => _lines.Add(new StoreRequisitionLine(itemId, quantity));

    /// <summary>Approves the requisition; never by the requester.</summary>
    /// <param name="userId">The approver.</param>
    /// <returns>Whether it was approved.</returns>
    public bool Approve(long userId)
    {
        if (Status != StoreRequisitionStatus.Requested || userId == RequestedByUserId)
        {
            return false;
        }

        ApprovedByUserId = userId;
        Status = StoreRequisitionStatus.Approved;
        return true;
    }

    /// <summary>Rejects the requisition.</summary>
    public void Reject() => Status = StoreRequisitionStatus.Rejected;

    /// <summary>Records goods issued against a line and updates the status.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">The quantity issued.</param>
    public void RecordIssue(long itemId, decimal quantity)
    {
        _lines.First(l => l.ItemId == itemId).RecordIssue(quantity);
        Status = _lines.All(l => l.Issued >= l.Requested) ? StoreRequisitionStatus.Issued : StoreRequisitionStatus.PartIssued;
    }
}

/// <summary>Goods on a store requisition (<c>inv.store_requisition_line</c>).</summary>
public sealed class StoreRequisitionLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private StoreRequisitionLine()
    {
    }

    /// <summary>Creates a line.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="requested">The quantity asked for.</param>
    public StoreRequisitionLine(long itemId, decimal requested)
    {
        ItemId = itemId;
        Requested = requested;
    }

    /// <summary>The requisition.</summary>
    public long StoreRequisitionId { get; private set; }

    /// <summary>The item.</summary>
    public long ItemId { get; private set; }

    /// <summary>The quantity asked for.</summary>
    public decimal Requested { get; private set; }

    /// <summary>The quantity issued so far.</summary>
    public decimal Issued { get; private set; }

    /// <summary>What is still to issue.</summary>
    public decimal Outstanding => Requested - Issued;

    /// <summary>Records goods issued.</summary>
    /// <param name="quantity">The quantity.</param>
    internal void RecordIssue(decimal quantity) => Issued += quantity;
}
