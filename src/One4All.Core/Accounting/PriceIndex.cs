using One4All.Core.Common;

namespace One4All.Core.Accounting;

/// <summary>How an account is treated when restating for hyperinflation (IAS 29).</summary>
public enum Ias29Classification
{
    /// <summary>By account type: assets and liabilities are monetary (except stock), equity, income and expenses are not.</summary>
    Default = 0,

    /// <summary>Monetary: already in current purchasing power (cash, receivables, payables, loans); not restated.</summary>
    Monetary = 1,

    /// <summary>Non-monetary: restated by the change in the price index since each amount was recorded (property, stock, prepayments).</summary>
    NonMonetary = 2,
}

/// <summary>
/// A general price index for one month (<c>gl.price_index</c>), such as the consumer price index used to restate
/// financial statements under IAS 29 (brief §3, optional hyperinflation module).
/// </summary>
public sealed class PriceIndex : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PriceIndex()
    {
        IndexCode = string.Empty;
    }

    /// <summary>Creates an index value.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="indexCode">The index, such as CPI.</param>
    /// <param name="month">Any date in the month; stored as the first of the month.</param>
    /// <param name="value">The index value.</param>
    public PriceIndex(long companyId, string indexCode, DateOnly month, decimal value)
    {
        CompanyId = companyId;
        IndexCode = indexCode.Trim().ToUpperInvariant();
        Month = new DateOnly(month.Year, month.Month, 1);
        Value = value;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The index, such as CPI.</summary>
    public string IndexCode { get; private set; }

    /// <summary>The first day of the month the value is for.</summary>
    public DateOnly Month { get; private set; }

    /// <summary>The index value.</summary>
    public decimal Value { get; set; }
}
