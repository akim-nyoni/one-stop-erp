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

    /// <summary>
    /// The withholding code to apply to a payment to a supplier (<see cref="WithholdingCodeAsync"/>), but only once the supplier's payments
    /// in the calendar year reach the company's <see cref="Core.System.Company.WithholdingThreshold"/>: the year's earlier payments (reversed
    /// ones left out) with this one. The payment that reaches it is withheld in full, and so is every later one that year.
    /// </summary>
    /// <param name="db">The database context.</param>
    /// <param name="companyId">The company.</param>
    /// <param name="supplier">The supplier paid.</param>
    /// <param name="date">The payment date.</param>
    /// <param name="requested">The code the payment asks for, or null for the company's default.</param>
    /// <param name="paymentBase">The gross payment in base currency.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The code, or null when nothing is withheld.</returns>
    public static async Task<string?> WithholdingCodeForPaymentAsync(One4AllDbContext db, long companyId, Supplier supplier, DateOnly date, string? requested, decimal paymentBase,
        CancellationToken ct)
    {
        string? code = await WithholdingCodeAsync(db, companyId, supplier, date, requested, ct).ConfigureAwait(false);
        return code is null || await ReachesThresholdAsync(db, companyId, supplier.Id, date, paymentBase, ct).ConfigureAwait(false) ? code : null;
    }

    /// <summary>Whether the supplier's payments in the calendar year of the date, with this one, reach the company's withholding threshold.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="companyId">The company.</param>
    /// <param name="supplierId">The supplier.</param>
    /// <param name="date">The payment date.</param>
    /// <param name="paymentBase">The gross payment in base currency.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when withholding applies to the payment.</returns>
    public static async Task<bool> ReachesThresholdAsync(One4AllDbContext db, long companyId, long supplierId, DateOnly date, decimal paymentBase, CancellationToken ct)
    {
        decimal threshold = await db.Companies.Where(c => c.Id == companyId).Select(c => c.WithholdingThreshold).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (threshold <= 0m || paymentBase >= threshold)
        {
            return true;
        }

        DateOnly yearStart = new(date.Year, 1, 1);
        decimal paid = await db.OpenItems
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsPayable && i.TraderId == supplierId && i.ItemType == OpenItemType.Payment
                && i.DocumentDate >= yearStart && i.DocumentDate <= date
                && !db.Journals.Any(r => r.CompanyId == companyId && r.ReversalOfJournalId == i.JournalEntryId))
            .SumAsync(i => i.OriginalBase, ct).ConfigureAwait(false);
        return paid + paymentBase >= threshold;
    }
}
