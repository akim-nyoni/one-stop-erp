using Microsoft.EntityFrameworkCore;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>A gross (tax-inclusive) amount split by a tax code into its net and its tax, with the account the tax goes to.</summary>
/// <param name="Code">The tax code.</param>
/// <param name="Net">The net.</param>
/// <param name="Tax">The tax.</param>
/// <param name="TaxAccountCode">Where the tax posts: the code's input (purchases) or output (sales) account, else the VAT control.</param>
internal sealed record GrossTaxSplit(string Code, decimal Net, decimal Tax, string TaxAccountCode)
{
    // Splits money paid or received with VAT in it (brief §3.5 VAT on cash-book postings): the code's rate on the date, the tax to its input account on a
    // payment or its output account on a receipt.
    public static async Task<Result<GrossTaxSplit>> SplitAsync(One4AllDbContext db, long companyId, string taxCode, decimal gross, DateOnly date, bool purchase, CancellationToken ct)
    {
        string key = taxCode.Trim().ToUpperInvariant();
        TaxCode? code = await db.TaxCodes.AsNoTracking().Include(t => t.Rates).FirstOrDefaultAsync(t => t.CompanyId == companyId && t.Code == key, ct).ConfigureAwait(false);
        if (code is null)
        {
            return Error.NotFound("Tax.CodeNotFound", $"No tax code {key}.");
        }

        decimal rate = code.RateOn(date);
        decimal net = decimal.Round(gross * 100m / (100m + rate), 2, MidpointRounding.AwayFromZero);
        string? account = (purchase ? code.InputAccountCode : code.OutputAccountCode)
            ?? await db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId && a.ControlAccountType == (purchase ? ControlAccountType.VatInput : ControlAccountType.VatOutput))
                .OrderBy(a => a.AccountCode).Select(a => a.AccountCode).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (account is null && gross != net)
        {
            return Error.Validation("Tax.NoTaxAccount", $"Tax code {key} has no {(purchase ? "input" : "output")} tax account to post to.");
        }

        return new GrossTaxSplit(key, net, gross - net, account ?? string.Empty);
    }
}
