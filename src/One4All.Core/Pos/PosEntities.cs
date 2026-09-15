using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.Pos;

/// <summary>The tender types accepted at POS (brief §4.1).</summary>
public enum TenderType
{
    /// <summary>Cash (multi-currency, denomination entry).</summary>
    Cash = 1,

    /// <summary>Card (terminal integration).</summary>
    Card = 2,

    /// <summary>Mobile money / wallet.</summary>
    MobileMoney = 3,

    /// <summary>Bank transfer.</summary>
    BankTransfer = 4,

    /// <summary>Cheque.</summary>
    Cheque = 5,

    /// <summary>Voucher / gift card.</summary>
    Voucher = 6,

    /// <summary>On account (credit — posts to AR).</summary>
    Credit = 7,
}

/// <summary>The status of a till session.</summary>
public enum TillSessionStatus
{
    /// <summary>Open and trading.</summary>
    Open = 0,

    /// <summary>Closed (Z-read taken).</summary>
    Closed = 1,
}

/// <summary>The status of a POS sale.</summary>
public enum SaleStatus
{
    /// <summary>Completed and posted.</summary>
    Completed = 0,

    /// <summary>Parked/suspended.</summary>
    Parked = 1,

    /// <summary>Voided.</summary>
    Voided = 2,
}

/// <summary>A POS terminal / till (brief §4.1, <c>pos.terminal</c>).</summary>
public sealed class PosTerminal : SoftDeletableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PosTerminal()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Creates a terminal.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="branchId">The branch.</param>
    /// <param name="code">The terminal code.</param>
    /// <param name="name">The terminal name.</param>
    public PosTerminal(long companyId, long branchId, string code, string name)
    {
        CompanyId = companyId;
        BranchId = branchId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The branch.</summary>
    public long BranchId { get; private set; }

    /// <summary>The terminal code.</summary>
    public string Code { get; private set; }

    /// <summary>The terminal name.</summary>
    public string Name { get; set; }

    /// <summary>Whether the terminal is active.</summary>
    public bool IsActive { get; set; }
}

/// <summary>
/// A till/drawer session (brief §4.1): opened with a float, traded through, and closed with a Z-read. The
/// Z-read number is sequential and immutable per terminal.
/// </summary>
public sealed class TillSession : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private TillSession()
    {
    }

    /// <summary>Opens a till session.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="terminalId">The terminal.</param>
    /// <param name="cashierUserId">The cashier.</param>
    /// <param name="openedOnUtc">When opened.</param>
    /// <param name="openingFloat">The opening float (base currency).</param>
    public TillSession(long companyId, long terminalId, long cashierUserId, DateTimeOffset openedOnUtc, decimal openingFloat)
    {
        CompanyId = companyId;
        TerminalId = terminalId;
        CashierUserId = cashierUserId;
        OpenedOnUtc = openedOnUtc;
        OpeningFloat = openingFloat;
        Status = TillSessionStatus.Open;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The terminal.</summary>
    public long TerminalId { get; private set; }

    /// <summary>The cashier.</summary>
    public long CashierUserId { get; private set; }

    /// <summary>When opened.</summary>
    public DateTimeOffset OpenedOnUtc { get; private set; }

    /// <summary>When closed (Z-read), or null while open.</summary>
    public DateTimeOffset? ClosedOnUtc { get; private set; }

    /// <summary>The opening float.</summary>
    public decimal OpeningFloat { get; private set; }

    /// <summary>The sequential Z-read number, assigned on close.</summary>
    public int? ZReadNumber { get; private set; }

    /// <summary>The status.</summary>
    public TillSessionStatus Status { get; private set; }

    /// <summary>Closes the session with a Z-read number.</summary>
    /// <param name="closedOnUtc">When closed.</param>
    /// <param name="zReadNumber">The sequential Z number.</param>
    public void Close(DateTimeOffset closedOnUtc, int zReadNumber)
    {
        ClosedOnUtc = closedOnUtc;
        ZReadNumber = zReadNumber;
        Status = TillSessionStatus.Closed;
    }
}
