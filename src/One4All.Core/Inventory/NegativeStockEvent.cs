using One4All.Core.Common;

namespace One4All.Core.Inventory;

/// <summary>
/// Stock issued with none (or not enough) on hand (<c>inv.negative_stock_event</c>, brief §3.6): how much went short, why and who allowed it —
/// and, once a receipt covers it, the cost correction posted.
/// </summary>
public sealed class NegativeStockEvent : Entity, ICompanyScoped
{
    private NegativeStockEvent()
    {
        Reason = string.Empty;
    }

    /// <summary>Records the event.</summary>
    public NegativeStockEvent(long companyId, long itemId, long warehouseId, DateOnly date, decimal shortQuantity, decimal unitCostUsed, string? reference, string reason, long? authorisedByUserId,
        bool fromTill)
    {
        CompanyId = companyId;
        ItemId = itemId;
        WarehouseId = warehouseId;
        Date = date;
        ShortQuantity = shortQuantity;
        UnitCostUsed = unitCostUsed;
        Reference = reference;
        Reason = reason;
        AuthorisedByUserId = authorisedByUserId;
        FromTill = fromTill;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The item.</summary>
    public long ItemId { get; private set; }

    /// <summary>The warehouse.</summary>
    public long WarehouseId { get; private set; }

    /// <summary>The date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>How much more was issued than was on hand.</summary>
    public decimal ShortQuantity { get; private set; }

    /// <summary>The unit cost the shortfall was issued at.</summary>
    public decimal UnitCostUsed { get; private set; }

    /// <summary>The issuing document.</summary>
    public string? Reference { get; private set; }

    /// <summary>Why.</summary>
    public string Reason { get; private set; }

    /// <summary>Who allowed it (null for a till sale).</summary>
    public long? AuthorisedByUserId { get; private set; }

    /// <summary>Whether a till sale caused it.</summary>
    public bool FromTill { get; private set; }

    /// <summary>The journal correcting the shortfall's cost once a receipt covered it.</summary>
    public string? CorrectionJournalNumber { get; private set; }

    /// <summary>Records the correction.</summary>
    /// <param name="journalNumber">The correcting journal (empty when nothing needed correcting).</param>
    public void Correct(string journalNumber) => CorrectionJournalNumber = journalNumber;
}
