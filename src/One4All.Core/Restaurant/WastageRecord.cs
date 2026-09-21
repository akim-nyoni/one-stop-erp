using One4All.Core.Common;

namespace One4All.Core.Restaurant;

/// <summary>
/// A record of spoilage / waste of a stock ingredient (<c>res.wastage</c>). Reduces stock at moving-average
/// cost and books the loss to a wastage expense account (rather than cost of sales), for visibility.
/// </summary>
public sealed class WastageRecord : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private WastageRecord()
    {
        Reason = string.Empty;
        WastageAccountCode = string.Empty;
    }

    /// <summary>Creates a wastage record.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="itemId">The stock item wasted.</param>
    /// <param name="warehouseId">The warehouse it was issued from.</param>
    /// <param name="date">The wastage date.</param>
    /// <param name="quantity">The quantity wasted.</param>
    /// <param name="value">The moving-average value written off.</param>
    /// <param name="reason">The reason for the wastage.</param>
    /// <param name="wastageAccountCode">The wastage expense account charged.</param>
    public WastageRecord(long companyId, long itemId, long warehouseId, DateOnly date, decimal quantity, decimal value, string reason, string wastageAccountCode)
    {
        CompanyId = companyId;
        ItemId = itemId;
        WarehouseId = warehouseId;
        Date = date;
        Quantity = quantity;
        Value = value;
        Reason = reason;
        WastageAccountCode = wastageAccountCode;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The stock item wasted.</summary>
    public long ItemId { get; private set; }

    /// <summary>The warehouse it was issued from.</summary>
    public long WarehouseId { get; private set; }

    /// <summary>The wastage date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The quantity wasted.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>The moving-average value written off.</summary>
    public decimal Value { get; private set; }

    /// <summary>The reason for the wastage.</summary>
    public string Reason { get; private set; }

    /// <summary>The wastage expense account charged.</summary>
    public string WastageAccountCode { get; private set; }
}
