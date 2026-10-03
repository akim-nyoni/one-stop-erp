using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>Withholding tax returns, remittance and certificates (see <see cref="IWithholdingTaxService"/>).</summary>
public sealed class WithholdingTaxService : IWithholdingTaxService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The posting engine.</param>
    public WithholdingTaxService(One4AllDbContext db, IPostingEngine posting)
    {
        _db = db;
        _posting = posting;
    }

    /// <inheritdoc />
    public async Task<Result<WithholdingReturn>> ReturnAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        if (toDate < fromDate)
        {
            return Error.Validation("Wht.Period", "The period ends before it starts.");
        }

        List<WithholdingReturnLine> lines = await (
            from c in _db.Set<WithholdingCertificate>()
            join s in _db.Suppliers on c.SupplierId equals s.Id
            where c.CompanyId == companyId && c.PaymentDate >= fromDate && c.PaymentDate <= toDate
            orderby c.PaymentDate, c.CertificateNumber
            select new WithholdingReturnLine(c.Id, c.CertificateNumber, s.Code, s.Name, c.PaymentDate, c.TaxCode, c.GrossAmount, c.WithheldAmount, c.RemittedOn))
            .ToListAsync(ct).ConfigureAwait(false);

        return new WithholdingReturn(fromDate, toDate, lines);
    }

    /// <inheritdoc />
    public Task<Result<WithholdingRemittance>> RemitAsync(long companyId, DateOnly fromDate, DateOnly toDate, DateOnly remittanceDate, string bankAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => RemitCoreAsync(companyId, fromDate, toDate, remittanceDate, bankAccountCode, ct), ct);

    // One unit of work: the remittance journal and the certificates it marks.
    private async Task<Result<WithholdingRemittance>> RemitCoreAsync(long companyId, DateOnly fromDate, DateOnly toDate, DateOnly remittanceDate, string bankAccountCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(bankAccountCode))
        {
            return Error.Validation("Wht.BankAccount", "The bank account is required.");
        }

        List<WithholdingCertificate> outstanding = await _db.Set<WithholdingCertificate>()
            .Where(c => c.CompanyId == companyId && c.PaymentDate >= fromDate && c.PaymentDate <= toDate && c.RemittedOn == null)
            .ToListAsync(ct).ConfigureAwait(false);
        if (outstanding.Count == 0)
        {
            return new WithholdingRemittance(0, 0m, null);
        }

        // Each withholding code's payable account is cleared for what it holds.
        List<string> codes = [.. outstanding.Select(c => c.TaxCode).Distinct()];
        Dictionary<string, string?> payable = await _db.TaxCodes
            .Where(t => t.CompanyId == companyId && codes.Contains(t.Code))
            .ToDictionaryAsync(t => t.Code, t => t.OutputAccountCode, ct).ConfigureAwait(false);

        List<JournalLineRequest> lines = [];
        foreach (IGrouping<string, WithholdingCertificate> byCode in outstanding.GroupBy(c => c.TaxCode))
        {
            if (payable.GetValueOrDefault(byCode.Key) is not { Length: > 0 } account)
            {
                return Error.Rule("Wht.NoPayableAccount", $"Withholding tax code {byCode.Key} has no payable account.");
            }

            lines.Add(new JournalLineRequest { AccountCode = account, Side = JournalSide.Debit, TransactionAmount = byCode.Sum(c => c.WithheldAmount) });
        }

        decimal total = outstanding.Sum(c => c.WithheldAmount);
        lines.Add(new JournalLineRequest { AccountCode = bankAccountCode.Trim(), Side = JournalSide.Credit, TransactionAmount = total });

        Company company = await _db.Companies.FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "AP",
            PostingDate = remittanceDate,
            TransactionCurrency = company.BaseCurrency,
            EnforceManualJournalRules = false,
            DocumentType = "WithholdingRemittance",
            DocumentNumber = $"WHTR-{toDate:yyyyMMdd}",
            Narrative = $"Withholding tax for {fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd} paid over",
            Lines = lines,
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        foreach (WithholdingCertificate certificate in outstanding)
        {
            certificate.MarkRemitted(remittanceDate, posted.Value.JournalNumber);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new WithholdingRemittance(outstanding.Count, total, posted.Value.JournalNumber);
    }

    /// <inheritdoc />
    public async Task<Result<string>> CertificateTextAsync(long companyId, long certificateId, CancellationToken ct = default)
    {
        WithholdingCertificate? certificate = await _db.Set<WithholdingCertificate>().AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == certificateId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (certificate is null)
        {
            return Error.NotFound("Wht.CertificateNotFound", $"Withholding certificate {certificateId} not found.");
        }

        Supplier supplier = await _db.Suppliers.AsNoTracking().FirstAsync(s => s.Id == certificate.SupplierId, ct).ConfigureAwait(false);
        Company company = await _db.Companies.AsNoTracking().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        return WithholdingCertificateFormatter.ToPlainText(new WithholdingCertificateDocument
        {
            CompanyName = company.Name,
            CertificateNumber = certificate.CertificateNumber,
            SupplierCode = supplier.Code,
            SupplierName = supplier.Name,
            PaymentReference = certificate.PaymentDocumentNumber,
            PaymentDate = certificate.PaymentDate,
            Currency = company.BaseCurrency.Value,
            Gross = certificate.GrossAmount,
            RatePercent = certificate.RatePercent,
            Withheld = certificate.WithheldAmount,
        });
    }
}
