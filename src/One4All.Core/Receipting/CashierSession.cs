using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.Receipting;

/// <summary>A cashiering station / counter (brief §RCP-2, <c>rcp.station</c>).</summary>
public sealed class CashierStation : SoftDeletableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private CashierStation()
    {
        Code = string.Empty;
        Name = string.Empty;
        ReceiptPrefix = string.Empty;
    }

    /// <summary>Creates a station.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The station code.</param>
    /// <param name="name">The station name.</param>
    /// <param name="receiptPrefix">The receipt-number prefix (its own series).</param>
    public CashierStation(long companyId, string code, string name, string receiptPrefix)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        ReceiptPrefix = receiptPrefix.Trim().ToUpperInvariant();
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The station code.</summary>
    public string Code { get; private set; }

    /// <summary>The station name.</summary>
    public string Name { get; set; }

    /// <summary>The receipt-number prefix (its own series).</summary>
    public string ReceiptPrefix { get; private set; }

    /// <summary>Whether the station is active.</summary>
    public bool IsActive { get; set; }

    /// <summary>Gets or sets a value indicating whether the station is a field collector's handheld (rent, market stalls, parking…) rather than a counter (brief §RCP-8).</summary>
    public bool IsFieldCollector { get; set; }

    /// <summary>Gets or sets the route the collector works (for the route performance report).</summary>
    public string? RouteCode { get; set; }

    /// <summary>Gets or sets the first number of the pre-numbered receipt range issued to the collector (null: numbered from 1 without a cap).</summary>
    public int? ReceiptRangeFrom { get; set; }

    /// <summary>Gets or sets the last number of the range; a receipt past it is refused until a new range is issued.</summary>
    public int? ReceiptRangeTo { get; set; }

    /// <summary>The receipt number for the n-th receipt (1-based) at this station, or null when the range is used up.</summary>
    /// <param name="ordinal">How many receipts the station has issued, plus one.</param>
    /// <returns>The number, or null.</returns>
    public string? ReceiptNumber(int ordinal)
    {
        int number = (ReceiptRangeFrom ?? 1) + ordinal - 1;
        return ReceiptRangeTo is int last && number > last ? null : $"{ReceiptPrefix}-{number:D6}";
    }
}

/// <summary>The lifecycle of a cashier session (brief §RCP-3 lifecycle).</summary>
public enum SessionStatus
{
    /// <summary>Open and transacting.</summary>
    Open = 1,

    /// <summary>Counted and closed by the cashier (awaiting verification).</summary>
    Closed = 2,

    /// <summary>Verified by a supervisor — immutable.</summary>
    Verified = 3,
}

/// <summary>
/// A cashier's shift at a station (brief §RCP-2, <c>rcp.session</c>): opening float per currency, then a
/// blind cash-up that computes the variance. One open session per cashier per station.
/// </summary>
public sealed class CashierSession : AuditableEntity, ICompanyScoped
{
    private readonly List<SessionFloat> _floats = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private CashierSession()
    {
    }

    /// <summary>Opens a session.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="stationId">The station.</param>
    /// <param name="cashierUserId">The cashier.</param>
    /// <param name="openedAt">When the session opened.</param>
    public CashierSession(long companyId, long stationId, long cashierUserId, DateOnly openedAt)
    {
        CompanyId = companyId;
        StationId = stationId;
        CashierUserId = cashierUserId;
        OpenedOn = openedAt;
        Status = SessionStatus.Open;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The station.</summary>
    public long StationId { get; private set; }

    /// <summary>The cashier (user id).</summary>
    public long CashierUserId { get; private set; }

    /// <summary>The date the session opened.</summary>
    public DateOnly OpenedOn { get; private set; }

    /// <summary>The date the session closed.</summary>
    public DateOnly? ClosedOn { get; private set; }

    /// <summary>The supervisor who verified the session.</summary>
    public long? VerifiedByUserId { get; private set; }

    /// <summary>The status.</summary>
    public SessionStatus Status { get; private set; }

    /// <summary>The variance from the cash-up in base currency (counted − expected).</summary>
    public decimal CashUpVarianceBase { get; private set; }

    /// <summary>The opening floats per currency.</summary>
    public IReadOnlyList<SessionFloat> Floats => _floats.AsReadOnly();

    /// <summary>Declares an opening float in a currency.</summary>
    /// <param name="currency">The currency.</param>
    /// <param name="amount">The float amount.</param>
    public void DeclareFloat(CurrencyCode currency, decimal amount) => _floats.Add(new SessionFloat(currency, amount));

    /// <summary>Gets the opening float for a currency (zero if none declared).</summary>
    /// <param name="currency">The currency.</param>
    /// <returns>The float amount.</returns>
    public decimal FloatFor(CurrencyCode currency) => _floats.Where(f => f.Currency == currency).Sum(f => f.Amount);

    /// <summary>Closes the session, recording the base-currency variance.</summary>
    /// <param name="closedOn">The close date.</param>
    /// <param name="varianceBase">The base-currency variance (counted − expected).</param>
    public void Close(DateOnly closedOn, decimal varianceBase)
    {
        ClosedOn = closedOn;
        CashUpVarianceBase = varianceBase;
        Status = SessionStatus.Closed;
    }

    /// <summary>Marks the session verified by a supervisor.</summary>
    /// <param name="supervisorUserId">The verifying supervisor.</param>
    public void Verify(long supervisorUserId)
    {
        VerifiedByUserId = supervisorUserId;
        Status = SessionStatus.Verified;
    }
}

/// <summary>An opening float in a currency (<c>rcp.session_float</c>).</summary>
public sealed class SessionFloat : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private SessionFloat()
    {
    }

    /// <summary>Creates a float line.</summary>
    /// <param name="currency">The currency.</param>
    /// <param name="amount">The amount.</param>
    public SessionFloat(CurrencyCode currency, decimal amount)
    {
        Currency = currency;
        Amount = amount;
    }

    /// <summary>The owning session id.</summary>
    public long CashierSessionId { get; private set; }

    /// <summary>The currency.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>The float amount.</summary>
    public decimal Amount { get; private set; }
}
