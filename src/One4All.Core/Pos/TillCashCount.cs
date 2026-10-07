using One4All.Core.Common;

namespace One4All.Core.Pos;

/// <summary>
/// Cash counted in one currency at a till's cash-up (<c>pos.till_cash_count</c>, brief §4.1). For a blind cash-up it is declared before
/// the expected cash is shown and cannot be changed; otherwise it is recorded at close. Kept as the audit of what was counted.
/// </summary>
public sealed class TillCashCount : Entity, ICompanyScoped
{
    private TillCashCount()
    {
        Currency = string.Empty;
    }

    /// <summary>Records the count.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="tillSessionId">The till session.</param>
    /// <param name="currency">The currency counted.</param>
    /// <param name="amount">The amount counted.</param>
    /// <param name="countedByUserId">Who counted it.</param>
    /// <param name="countedOnUtc">When.</param>
    /// <param name="blind">Whether it was declared blind.</param>
    public TillCashCount(long companyId, long tillSessionId, string currency, decimal amount, long countedByUserId, DateTimeOffset countedOnUtc, bool blind)
    {
        CompanyId = companyId;
        TillSessionId = tillSessionId;
        Currency = currency;
        Amount = amount;
        CountedByUserId = countedByUserId;
        CountedOnUtc = countedOnUtc;
        Blind = blind;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The till session.</summary>
    public long TillSessionId { get; private set; }

    /// <summary>The currency counted.</summary>
    public string Currency { get; private set; }

    /// <summary>The amount counted.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Who counted it.</summary>
    public long CountedByUserId { get; private set; }

    /// <summary>When.</summary>
    public DateTimeOffset CountedOnUtc { get; private set; }

    /// <summary>Whether it was declared blind (before the expected cash was shown).</summary>
    public bool Blind { get; private set; }
}
