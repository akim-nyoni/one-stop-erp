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

    /// <summary>On account (credit - posts to AR).</summary>
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

    /// <summary>Whether the till is cashed up blind (brief §4.1): the cashier declares the count before seeing what was expected.</summary>
    public bool BlindCashUp { get; set; }

    /// <summary>The terminal's profile - what kind of counter it is, which shapes the till's layout (brief §4.1 "configurable layout per terminal profile").</summary>
    public TerminalProfile Profile { get; set; }

    /// <summary>Gets or sets the terminal's own function keys ("F2=TenderCash; F3=TenderCard"), over its profile's defaults.</summary>
    public string? FunctionKeys { get; set; }

    /// <summary>The cash-up variance (base currency, either way) above which a supervisor must sign the cash-up off; null for never.</summary>
    public decimal? CashUpSignOffVariance { get; set; }
}

/// <summary>What kind of counter a terminal is.</summary>
public enum TerminalProfile
{
    /// <summary>A general till.</summary>
    General = 0,

    /// <summary>A supermarket checkout (scale and scanner first).</summary>
    Supermarket = 1,

    /// <summary>A bar (favourites and tabs first; no scale).</summary>
    Bar = 2,

    /// <summary>A butchery or deli counter (the scale first).</summary>
    Butchery = 3,

    /// <summary>A weighbridge kiosk.</summary>
    WeighbridgeKiosk = 4,

    /// <summary>A restaurant till (favourites and tabs first; no scale).</summary>
    Restaurant = 5,
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

    /// <summary>Gets or sets the key a till opened offline was given at the terminal (its sales find it by this when they sync).</summary>
    public string? OfflineKey { get; set; }

    /// <summary>The sequential Z-read number, assigned on close.</summary>
    public int? ZReadNumber { get; private set; }

    /// <summary>The supervisor who signed off the cash-up variance, when one was required.</summary>
    public long? SignedOffByUserId { get; private set; }

    /// <summary>Records the supervisor's sign-off of the cash-up.</summary>
    /// <param name="userId">The supervisor.</param>
    public void SignOff(long userId) => SignedOffByUserId = userId;

    /// <summary>The status.</summary>
    public TillSessionStatus Status { get; private set; }

    /// <summary>Passes the open session to another cashier without closing it (the quick switch; the drawer stays shared).</summary>
    /// <param name="cashierUserId">The incoming cashier.</param>
    public void Reassign(long cashierUserId)
    {
        if (Status != TillSessionStatus.Open)
        {
            throw new InvalidOperationException("Only an open till session can pass to another cashier.");
        }

        CashierUserId = cashierUserId;
    }

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
