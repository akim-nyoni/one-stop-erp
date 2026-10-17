using Microsoft.EntityFrameworkCore;
using One4All.Core.Accounting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>
/// The certificate checks every posting path shares (brief §3.5): whether a trader holds a valid exemption certificate, and whether a
/// supplier is tax-cleared - by its clearance date or a clearance certificate - and so free of withholding on a payment.
/// </summary>
internal static class TraderCertificates
{
    /// <summary>Whether a valid, unrevoked certificate of the purpose covers the date.</summary>
    public static Task<bool> HoldsAsync(One4AllDbContext db, long companyId, TradingLedger ledger, long traderId, CertificatePurpose purpose, DateOnly date, CancellationToken ct)
        => db.TaxExemptionCertificates.AnyAsync(c => c.CompanyId == companyId && c.Ledger == ledger && c.TraderId == traderId && c.Purpose == purpose
            && c.ValidFrom <= date && c.ValidTo >= date && (c.RevokedOn == null || c.RevokedOn > date), ct);

    /// <summary>Whether the supplier is tax-cleared on the date (its clearance expiry, or a clearance certificate).</summary>
    public static async Task<bool> IsClearedAsync(One4AllDbContext db, long companyId, Supplier supplier, DateOnly date, CancellationToken ct)
        => (supplier.TaxClearanceExpiry is { } expiry && expiry >= date)
            || await HoldsAsync(db, companyId, TradingLedger.AccountsPayable, supplier.Id, CertificatePurpose.TaxClearance, date, ct).ConfigureAwait(false);

    /// <summary>
    /// The withholding code to apply to a payment: none when the supplier is cleared; otherwise the one asked for, or the company's
    /// default for uncleared suppliers.
    /// </summary>
    public static async Task<string?> WithholdingCodeAsync(One4AllDbContext db, long companyId, Supplier supplier, DateOnly date, string? requested, CancellationToken ct)
    {
        if (await IsClearedAsync(db, companyId, supplier, date, ct).ConfigureAwait(false))
        {
            return null;
        }

        string? code = string.IsNullOrWhiteSpace(requested)
            ? await db.Companies.Where(c => c.Id == companyId).Select(c => c.WithholdingWithoutClearanceTaxCode).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            : requested;
        return string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();
    }
}
