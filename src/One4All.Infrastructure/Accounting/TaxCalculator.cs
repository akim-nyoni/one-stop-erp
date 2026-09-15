using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>Calculates tax from a company's effective-dated tax codes (brief §3.5).</summary>
public sealed class TaxCalculator : ITaxCalculator
{
    private const int Scale = 4;
    private readonly One4AllDbContext _db;

    /// <summary>Creates the calculator.</summary>
    /// <param name="db">The database context.</param>
    public TaxCalculator(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<TaxCalculation>> OnNetAsync(long companyId, string taxCode, decimal netAmount, DateOnly taxPointDate, CancellationToken ct = default)
    {
        Result<TaxCode> codeResult = await LoadAsync(companyId, taxCode, ct).ConfigureAwait(false);
        if (codeResult.IsFailure)
        {
            return codeResult.Error;
        }

        TaxCode code = codeResult.Value;
        decimal rate = code.RateOn(taxPointDate);
        decimal tax = decimal.Round(netAmount * rate / 100m, Scale, MidpointRounding.ToEven);
        return Build(code, rate, netAmount, tax);
    }

    /// <inheritdoc />
    public async Task<Result<TaxCalculation>> FromGrossAsync(long companyId, string taxCode, decimal grossAmount, DateOnly taxPointDate, CancellationToken ct = default)
    {
        Result<TaxCode> codeResult = await LoadAsync(companyId, taxCode, ct).ConfigureAwait(false);
        if (codeResult.IsFailure)
        {
            return codeResult.Error;
        }

        TaxCode code = codeResult.Value;
        decimal rate = code.RateOn(taxPointDate);
        decimal net = rate == 0m ? grossAmount : decimal.Round(grossAmount / (1m + rate / 100m), Scale, MidpointRounding.ToEven);
        decimal tax = grossAmount - net;
        return Build(code, rate, net, tax);
    }

    private static Result<TaxCalculation> Build(TaxCode code, decimal rate, decimal net, decimal tax)
    {
        decimal recoverable = decimal.Round(tax * code.RecoverablePercent / 100m, Scale, MidpointRounding.ToEven);
        return new TaxCalculation(code.Code, code.Type, rate, net, tax, recoverable);
    }

    private async Task<Result<TaxCode>> LoadAsync(long companyId, string taxCode, CancellationToken ct)
    {
        string code = taxCode.Trim().ToUpperInvariant();
        TaxCode? found = await _db.Set<TaxCode>()
            .Include(t => t.Rates)
            .FirstOrDefaultAsync(t => t.CompanyId == companyId && t.Code == code, ct)
            .ConfigureAwait(false);

        return found is null
            ? Error.NotFound("Tax.CodeNotFound", $"Tax code '{code}' not found for company {companyId}.")
            : found;
    }
}
