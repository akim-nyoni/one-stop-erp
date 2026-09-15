using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Accounting;

/// <summary>The result of a tax calculation.</summary>
/// <param name="TaxCode">The tax code applied.</param>
/// <param name="Type">The tax type.</param>
/// <param name="RatePercent">The rate percentage in force at the tax point.</param>
/// <param name="NetAmount">The net (tax-exclusive) amount.</param>
/// <param name="TaxAmount">The tax amount.</param>
/// <param name="RecoverableTax">The recoverable portion of the tax (input recovery).</param>
public readonly record struct TaxCalculation(string TaxCode, TaxType Type, decimal RatePercent, decimal NetAmount, decimal TaxAmount, decimal RecoverableTax)
{
    /// <summary>The gross (tax-inclusive) amount.</summary>
    public decimal GrossAmount => NetAmount + TaxAmount;
}

/// <summary>
/// Calculates tax from a company's effective-dated tax codes (brief §3.5). The rate is resolved at the
/// tax-point date, so back-dated documents use the historical rate.
/// </summary>
public interface ITaxCalculator
{
    /// <summary>Calculates tax on a net (tax-exclusive) amount.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="taxCode">The tax code.</param>
    /// <param name="netAmount">The net amount.</param>
    /// <param name="taxPointDate">The tax-point date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The tax calculation, or an error if the code is unknown.</returns>
    Task<Result<TaxCalculation>> OnNetAsync(long companyId, string taxCode, decimal netAmount, DateOnly taxPointDate, CancellationToken ct = default);

    /// <summary>Extracts the tax from a gross (tax-inclusive) amount (e.g. a POS price).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="taxCode">The tax code.</param>
    /// <param name="grossAmount">The gross amount.</param>
    /// <param name="taxPointDate">The tax-point date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The tax calculation, or an error if the code is unknown.</returns>
    Task<Result<TaxCalculation>> FromGrossAsync(long companyId, string taxCode, decimal grossAmount, DateOnly taxPointDate, CancellationToken ct = default);
}
