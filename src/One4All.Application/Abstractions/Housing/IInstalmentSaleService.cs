using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A sale of a stand on instalments.</summary>
public sealed class InstalmentSaleRequest
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The stand.</summary>
    public long StandId { get; init; }

    /// <summary>The buyer's customer account.</summary>
    public long BuyerCustomerId { get; init; }

    /// <summary>The cash price.</summary>
    public decimal CashPrice { get; init; }

    /// <summary>The deposit paid on signature.</summary>
    public decimal Deposit { get; init; }

    /// <summary>The annual interest rate.</summary>
    public decimal AnnualRatePercent { get; init; }

    /// <summary>How interest is charged.</summary>
    public InterestMethod Method { get; init; }

    /// <summary>The term in months.</summary>
    public int TermMonths { get; init; }

    /// <summary>The signature date: control passes and the sale is recognised.</summary>
    public DateOnly SignedOn { get; init; }

    /// <summary>The first instalment's due date.</summary>
    public DateOnly FirstDueDate { get; init; }

    /// <summary>The tenure the instalments buy.</summary>
    public One4All.Core.Housing.InstalmentTenure Tenure { get; init; } = One4All.Core.Housing.InstalmentTenure.InstalmentSale;

    /// <summary>The stand sales revenue account.</summary>
    public string SalesAccountCode { get; init; } = string.Empty;

    /// <summary>The stands-available inventory account.</summary>
    public string StandsAvailableAccountCode { get; init; } = string.Empty;

    /// <summary>The cost-of-sales account.</summary>
    public string CostOfSalesAccountCode { get; init; } = string.Empty;

    /// <summary>The interest income account.</summary>
    public string InterestAccountCode { get; init; } = string.Empty;

    /// <summary>The bank account the deposit is banked to.</summary>
    public string BankAccountCode { get; init; } = string.Empty;

    /// <summary>The account a financing-component discount is deferred in (needed when the plan runs over a year below the market rate).</summary>
    public string? DeferredFinancingAccountCode { get; init; }
}

/// <summary>An instalment sale made.</summary>
/// <param name="AgreementNumber">The agreement number (also the sale invoice).</param>
/// <param name="Schedule">The instalments.</param>
public sealed record InstalmentSaleResult(string AgreementNumber, IReadOnlyList<PlannedInstalment> Schedule);

/// <summary>A run that raises instalments falling due.</summary>
/// <param name="InstalmentsRaised">The instalments that fell due.</param>
/// <param name="InterestRaised">The interest income raised on them.</param>
public sealed record InstalmentRunResult(int InstalmentsRaised, decimal InterestRaised);

/// <summary>
/// Instalment sales of stands (brief §4.10.5, accounted per §4.10.9): the sale is recognised at the cash price when the
/// agreement is signed (full receivable, cost of sales released) and the deposit receipted; interest on the plan is a
/// financing component raised as income as instalments fall due; early settlement rebates interest not yet due.
/// </summary>
public interface IInstalmentSaleService
{
    /// <summary>Signs an instalment sale: stand sold at the cash price, deposit receipted, schedule recorded. One unit.</summary>
    /// <param name="request">The sale.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The agreement and its schedule, or why it was refused.</returns>
    Task<Result<InstalmentSaleResult>> SignAsync(InstalmentSaleRequest request, CancellationToken ct = default);

    /// <summary>Raises the instalments falling due by a date, invoicing their interest. Re-running raises nothing twice.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="asAtDate">The date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was raised.</returns>
    Task<Result<InstalmentRunResult>> RaiseDueAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default);

    /// <summary>Quotes early settlement: the principal still owed and the interest that would be rebated.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="agreementNumber">The agreement.</param>
    /// <param name="asAtDate">The settlement date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The quote, or why there is none.</returns>
    Task<Result<SettlementQuote>> QuoteSettlementAsync(long companyId, string agreementNumber, DateOnly asAtDate, CancellationToken ct = default);

    /// <summary>Settles early: instalments due by the date are raised, later interest is rebated and never charged.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="agreementNumber">The agreement.</param>
    /// <param name="asAtDate">The settlement date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The settlement figures, or why it was refused.</returns>
    Task<Result<SettlementQuote>> SettleEarlyAsync(long companyId, string agreementNumber, DateOnly asAtDate, CancellationToken ct = default);
}
