using One4All.Core.Common;

namespace One4All.Core.System;

/// <summary>A rate keyed on a document instead of the table's (<c>sys.rate_override</c>, brief §3.3), kept for audit.</summary>
public sealed class ExchangeRateOverride : Entity, ICompanyScoped
{
    private ExchangeRateOverride()
    {
        DocumentNumber = string.Empty;
        Currency = string.Empty;
    }

    /// <summary>Records the override.</summary>
    public ExchangeRateOverride(long companyId, string documentNumber, string currency, DateOnly date, decimal keyedRate, decimal? tableRate, long userId, DateTimeOffset at)
    {
        CompanyId = companyId;
        DocumentNumber = documentNumber;
        Currency = currency;
        Date = date;
        KeyedRate = keyedRate;
        TableRate = tableRate;
        UserId = userId;
        At = at;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The document.</summary>
    public string DocumentNumber { get; private set; }

    /// <summary>The document currency.</summary>
    public string Currency { get; private set; }

    /// <summary>The document date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The rate keyed.</summary>
    public decimal KeyedRate { get; private set; }

    /// <summary>The rate the table would have given (null: none found).</summary>
    public decimal? TableRate { get; private set; }

    /// <summary>Who keyed it.</summary>
    public long UserId { get; private set; }

    /// <summary>When.</summary>
    public DateTimeOffset At { get; private set; }
}
