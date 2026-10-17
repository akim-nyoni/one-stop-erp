using One4All.Core.Common;

namespace One4All.Core.Pos;

/// <summary>Where a parked sale stands.</summary>
public enum ParkedSaleStatus
{
    /// <summary>Held, waiting to be resumed.</summary>
    Parked = 0,

    /// <summary>Taken back up at a till.</summary>
    Resumed = 1,

    /// <summary>Abandoned.</summary>
    Discarded = 2,
}

/// <summary>
/// A sale held part-way (<c>pos.parked_sale</c>, brief §4.1): its lines kept under a name, table or customer tag so it can be resumed -
/// at any till - or discarded with a reason. Any number may be parked at once.
/// </summary>
public sealed class ParkedSale : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ParkedSale()
    {
        Tag = string.Empty;
        LinesJson = string.Empty;
    }

    /// <summary>Parks a sale.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="terminalId">The till it was parked at.</param>
    /// <param name="tag">The name, table or customer tag.</param>
    /// <param name="linesJson">The lines, serialized.</param>
    /// <param name="total">The total so far.</param>
    /// <param name="userId">Who parked it.</param>
    /// <param name="parkedAt">When.</param>
    public ParkedSale(long companyId, long terminalId, string tag, string linesJson, decimal total, long userId, DateTimeOffset parkedAt)
    {
        CompanyId = companyId;
        TerminalId = terminalId;
        Tag = tag;
        LinesJson = linesJson;
        Total = total;
        ParkedByUserId = userId;
        ParkedAt = parkedAt;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The till it was parked at.</summary>
    public long TerminalId { get; private set; }

    /// <summary>The name, table or customer tag.</summary>
    public string Tag { get; private set; }

    /// <summary>The lines, serialized.</summary>
    public string LinesJson { get; private set; }

    /// <summary>The total so far.</summary>
    public decimal Total { get; private set; }

    /// <summary>Who parked it.</summary>
    public long ParkedByUserId { get; private set; }

    /// <summary>When it was parked.</summary>
    public DateTimeOffset ParkedAt { get; private set; }

    /// <summary>The status.</summary>
    public ParkedSaleStatus Status { get; private set; }

    /// <summary>Who resumed or discarded it.</summary>
    public long? ClosedByUserId { get; private set; }

    /// <summary>The till it was resumed at.</summary>
    public long? ResumedAtTerminalId { get; private set; }

    /// <summary>Why it was discarded.</summary>
    public string? DiscardReason { get; private set; }

    /// <summary>Takes it back up at a till.</summary>
    /// <param name="terminalId">The till.</param>
    /// <param name="userId">Who.</param>
    public void Resume(long terminalId, long userId)
    {
        Status = ParkedSaleStatus.Resumed;
        ResumedAtTerminalId = terminalId;
        ClosedByUserId = userId;
    }

    /// <summary>Abandons it.</summary>
    /// <param name="userId">Who.</param>
    /// <param name="reason">Why.</param>
    public void Discard(long userId, string reason)
    {
        Status = ParkedSaleStatus.Discarded;
        ClosedByUserId = userId;
        DiscardReason = reason;
    }
}
