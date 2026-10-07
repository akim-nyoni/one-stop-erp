using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Scheduling;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>Exemption certificates and the VAT return on the tax point basis (brief §3.5).</summary>
public sealed class TaxComplianceService : ITaxComplianceService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public TaxComplianceService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<long>> RecordCertificateAsync(long companyId, TradingLedger ledger, string traderCode, string certificateNumber, DateOnly validFrom, DateOnly validTo, string? issuedBy,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(certificateNumber) || validTo < validFrom)
        {
            return Error.Validation("Tax.CertificateInvalid", "A certificate needs a number and a validity that does not end before it starts.");
        }

        string code = (traderCode ?? string.Empty).Trim().ToUpperInvariant();
        long? trader = ledger == TradingLedger.AccountsReceivable
            ? await _db.Customers.Where(c => c.CompanyId == companyId && c.Code.ToUpper() == code).Select(c => (long?)c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            : await _db.Suppliers.Where(s => s.CompanyId == companyId && s.Code.ToUpper() == code).Select(s => (long?)s.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (trader is null)
        {
            return Error.NotFound("Tax.TraderNotFound", $"{(ledger == TradingLedger.AccountsReceivable ? "Customer" : "Supplier")} {code} not found.");
        }

        TaxExemptionCertificate certificate = new(companyId, ledger, trader.Value, certificateNumber.Trim(), validFrom, validTo) { IssuedBy = issuedBy?.Trim() };
        _db.TaxExemptionCertificates.Add(certificate);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return certificate.Id;
    }

    /// <inheritdoc />
    public async Task<Result> RevokeCertificateAsync(long companyId, long certificateId, DateOnly date, CancellationToken ct = default)
    {
        TaxExemptionCertificate? certificate = await _db.TaxExemptionCertificates.FirstOrDefaultAsync(c => c.Id == certificateId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (certificate is null || certificate.RevokedOn is not null)
        {
            return Result.Failure(Error.Rule("Tax.CertificateNotOpen", "No current certificate with that id."));
        }

        certificate.Revoke(date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExpiringCertificate>> ExpiringAsync(long companyId, DateOnly asAt, int withinDays = 30, CancellationToken ct = default)
    {
        DateOnly until = asAt.AddDays(withinDays), since = asAt.AddDays(-30);
        List<TaxExemptionCertificate> due = await _db.TaxExemptionCertificates.AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.RevokedOn == null && c.ValidTo <= until && c.ValidTo >= since).OrderBy(c => c.ValidTo).ToListAsync(ct).ConfigureAwait(false);

        // A trader who already holds a later certificate is not warned about the old one.
        List<TaxExemptionCertificate> later = await _db.TaxExemptionCertificates.AsNoTracking().Where(c => c.CompanyId == companyId && c.RevokedOn == null && c.ValidTo > until)
            .ToListAsync(ct).ConfigureAwait(false);
        List<long> customerIds = [.. due.Where(c => c.Ledger == TradingLedger.AccountsReceivable).Select(c => c.TraderId)];
        List<long> supplierIds = [.. due.Where(c => c.Ledger == TradingLedger.AccountsPayable).Select(c => c.TraderId)];
        Dictionary<long, (string Code, string Name)> customers = await _db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => (c.Code, c.Name), ct).ConfigureAwait(false);
        Dictionary<long, (string Code, string Name)> suppliers = await _db.Suppliers.AsNoTracking().Where(s => supplierIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => (s.Code, s.Name), ct).ConfigureAwait(false);
        return [.. due.Where(c => !later.Any(l => l.Ledger == c.Ledger && l.TraderId == c.TraderId && l.ValidFrom <= c.ValidTo.AddDays(1)))
            .Select(c =>
            {
                (string code, string name) = (c.Ledger == TradingLedger.AccountsReceivable ? customers : suppliers).GetValueOrDefault(c.TraderId, ("?", "?"));
                return new ExpiringCertificate(c.Id, c.Ledger, code, name, c.CertificateNumber, c.ValidTo, c.ValidTo.DayNumber - asAt.DayNumber);
            })];
    }

    /// <inheritdoc />
    public async Task<BasisVatReturn> VatReturnAsync(long companyId, DateOnly fromDate, DateOnly toDate, TaxPointBasis? basis = null, CancellationToken ct = default)
    {
        TaxPointBasis used = basis ?? await _db.Companies.Where(c => c.Id == companyId).Select(c => c.TaxPointBasis).SingleAsync(ct).ConfigureAwait(false);
        HashSet<string> outputs = [.. await _db.Accounts.Where(a => a.CompanyId == companyId && a.ControlAccountType == ControlAccountType.VatOutput).Select(a => a.AccountCode).ToListAsync(ct).ConfigureAwait(false)];
        HashSet<string> inputs = [.. await _db.Accounts.Where(a => a.CompanyId == companyId && a.ControlAccountType == ControlAccountType.VatInput).Select(a => a.AccountCode).ToListAsync(ct).ConfigureAwait(false)];
        List<string> vatAccounts = [.. outputs, .. inputs];

        // VAT per journal: posted in the period (the ledger), and for any document journal whatever its date.
        var vatLines = await (
                from line in _db.Set<JournalLine>()
                join journal in _db.Journals on line.JournalEntryId equals journal.Id
                where journal.CompanyId == companyId && vatAccounts.Contains(line.AccountCode) && (journal.Status == JournalStatus.Posted || journal.Status == JournalStatus.Reversed)
                select new { journal.Id, journal.PostingDate, line.AccountCode, line.BaseDebit, line.BaseCredit })
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, (DateOnly Date, decimal Output, decimal Input)> byJournal = vatLines.GroupBy(l => l.Id).ToDictionary(g => g.Key, g => (g.First().PostingDate,
            g.Where(l => outputs.Contains(l.AccountCode)).Sum(l => l.BaseCredit - l.BaseDebit), g.Where(l => inputs.Contains(l.AccountCode)).Sum(l => l.BaseDebit - l.BaseCredit)));
        bool InPeriod(DateOnly date) => date >= fromDate && date <= toDate;
        decimal ledgerOutput = byJournal.Values.Where(v => InPeriod(v.Date)).Sum(v => v.Output);
        decimal ledgerInput = byJournal.Values.Where(v => InPeriod(v.Date)).Sum(v => v.Input);
        List<(string, string, decimal, decimal)> adjustments = [];

        if (used != TaxPointBasis.InvoiceDate)
        {
            List<long> vatJournals = [.. byJournal.Keys];
            List<OpenItem> documents = await _db.OpenItems.AsNoTracking()
                .Where(o => o.CompanyId == companyId && (o.ItemType == OpenItemType.Invoice || o.ItemType == OpenItemType.CreditNote) && o.JournalEntryId != null && vatJournals.Contains(o.JournalEntryId.Value))
                .ToListAsync(ct).ConfigureAwait(false);

            if (used == TaxPointBasis.TaxPoint)
            {
                foreach (OpenItem document in documents.Where(d => d.TaxPointDate is not null))
                {
                    (DateOnly posted, decimal output, decimal input) = byJournal[document.JournalEntryId!.Value];
                    bool postedHere = InPeriod(posted), dueHere = InPeriod(document.TaxPointDate!.Value);
                    if (postedHere != dueHere)
                    {
                        decimal sign = dueHere ? 1m : -1m;
                        adjustments.Add((document.DocumentNumber, dueHere ? $"tax point {document.TaxPointDate:yyyy-MM-dd} in this period" : $"tax point {document.TaxPointDate:yyyy-MM-dd} in another period",
                            sign * output, sign * input));
                    }
                }
            }
            else
            {
                // Cash accounting: invoice and credit-note VAT leaves the period it was posted in and comes back as it is settled.
                foreach (OpenItem document in documents.Where(d => InPeriod(byJournal[d.JournalEntryId!.Value].Date)))
                {
                    (_, decimal output, decimal input) = byJournal[document.JournalEntryId!.Value];
                    adjustments.Add((document.DocumentNumber, "awaiting payment (cash accounting)", -output, -input));
                }

                Dictionary<long, OpenItem> byId = documents.ToDictionary(d => d.Id);
                List<long> ids = [.. byId.Keys];
                List<Allocation> settled = await _db.Set<Allocation>().AsNoTracking()
                    .Where(a => a.CompanyId == companyId && (ids.Contains(a.DebitItemId) || ids.Contains(a.CreditItemId))
                        && ((a.AllocationDate >= fromDate && a.AllocationDate <= toDate) || (a.IsReversed && a.ReversedOn >= fromDate && a.ReversedOn <= toDate)))
                    .ToListAsync(ct).ConfigureAwait(false);
                foreach (Allocation allocation in settled)
                {
                    foreach ((long itemId, decimal allocatedBase) in new[] { (allocation.DebitItemId, allocation.DebitBase), (allocation.CreditItemId, allocation.CreditBase) })
                    {
                        if (!byId.TryGetValue(itemId, out OpenItem? document) || document.OriginalBase == 0m)
                        {
                            continue;
                        }

                        (_, decimal output, decimal input) = byJournal[document.JournalEntryId!.Value];
                        decimal share = allocatedBase / document.OriginalBase;
                        decimal sign = (InPeriod(allocation.AllocationDate) ? 1m : 0m) - (allocation.IsReversed && allocation.ReversedOn is DateOnly undone && InPeriod(undone) ? 1m : 0m);
                        if (sign != 0m)
                        {
                            adjustments.Add((document.DocumentNumber, sign > 0 ? $"paid {allocation.AllocationDate:yyyy-MM-dd}" : $"payment undone {allocation.ReversedOn:yyyy-MM-dd}",
                                Math.Round(sign * output * share, 2), Math.Round(sign * input * share, 2)));
                        }
                    }
                }
            }
        }

        return new BasisVatReturn(used, fromDate, toDate, ledgerOutput, ledgerInput, ledgerOutput + adjustments.Sum(a => a.Item3), ledgerInput + adjustments.Sum(a => a.Item4), adjustments);
    }
}

/// <summary>Scheduled task: lists exemption certificates expiring within 30 days (key <c>tax.certificate-expiry</c>).</summary>
public sealed class CertificateExpiryTask : IScheduledTask
{
    private readonly ITaxComplianceService _tax;

    /// <summary>Creates the task.</summary>
    /// <param name="tax">The tax compliance service.</param>
    public CertificateExpiryTask(ITaxComplianceService tax) => _tax = tax;

    /// <inheritdoc />
    public string Key => "tax.certificate-expiry";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        IReadOnlyList<ExpiringCertificate> due = await _tax.ExpiringAsync(companyId, DateOnly.FromDateTime(runAt.UtcDateTime), 30, ct).ConfigureAwait(false);
        return due.Count == 0 ? "No exemption certificates expire within 30 days."
            : $"{due.Count} certificate(s) to renew: " + string.Join("; ", due.Select(d => $"{d.TraderCode} {d.CertificateNumber} {(d.DaysLeft < 0 ? "expired" : "expires")} {d.ValidTo:yyyy-MM-dd}"));
    }
}
