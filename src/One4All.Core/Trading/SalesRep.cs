using One4All.Core.Common;

namespace One4All.Core.Trading;

/// <summary>What a sales rep's commission is earned on.</summary>
public enum CommissionBasis
{
    /// <summary>Revenue invoiced to the rep's customers, net of tax, less credit notes.</summary>
    Invoiced = 1,

    /// <summary>Money collected from the rep's customers.</summary>
    Collected = 2,
}

/// <summary>
/// A sales rep (<c>ar.sales_rep</c>, brief §4.5): assigned to customers, earning commission at a rate on what those
/// customers are invoiced or pay. Commission is accrued monthly as an expense and a liability until paid.
/// </summary>
public sealed class SalesRep : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private SalesRep()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Creates a rep.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The rep's code.</param>
    /// <param name="name">The rep's name.</param>
    /// <param name="basis">What commission is earned on.</param>
    /// <param name="ratePercent">The commission rate.</param>
    public SalesRep(long companyId, string code, string name, CommissionBasis basis, decimal ratePercent)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        Basis = basis;
        RatePercent = ratePercent;
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The rep's code.</summary>
    public string Code { get; private set; }

    /// <summary>The rep's name.</summary>
    public string Name { get; set; }

    /// <summary>What commission is earned on.</summary>
    public CommissionBasis Basis { get; set; }

    /// <summary>The commission rate.</summary>
    public decimal RatePercent { get; set; }

    /// <summary>Whether the rep earns commission.</summary>
    public bool IsActive { get; set; }
}

/// <summary>
/// Commission accrued for a rep for a month (<c>ar.commission_accrual</c>): the month's commission booked so far, so a
/// rerun books only the difference.
/// </summary>
public sealed class CommissionAccrual : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private CommissionAccrual()
    {
    }

    /// <summary>Starts a month's accrual for a rep.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="salesRepId">The rep.</param>
    /// <param name="month">The first day of the month.</param>
    public CommissionAccrual(long companyId, long salesRepId, DateOnly month)
    {
        CompanyId = companyId;
        SalesRepId = salesRepId;
        Month = month;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The rep.</summary>
    public long SalesRepId { get; private set; }

    /// <summary>The first day of the month.</summary>
    public DateOnly Month { get; private set; }

    /// <summary>The commission booked for the month so far.</summary>
    public decimal Accrued { get; private set; }

    /// <summary>Records more (or less) commission booked.</summary>
    /// <param name="amount">The change.</param>
    public void Add(decimal amount) => Accrued += amount;
}
