using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>Builds customer invoice / credit note documents from the AR open item and the journal that posted it.</summary>
public sealed class InvoiceDocumentService : IInvoiceDocumentService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public InvoiceDocumentService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<InvoiceDocument>> BuildAsync(long companyId, long openItemId, CancellationToken ct = default)
    {
        OpenItem? item = await _db.OpenItems
            .FirstOrDefaultAsync(i => i.Id == openItemId && i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable, ct).ConfigureAwait(false);
        if (item is null)
        {
            return Error.NotFound("Ar.ItemNotFound", $"AR open item {openItemId} not found.");
        }

        if (item.ItemType is not (OpenItemType.Invoice or OpenItemType.CreditNote))
        {
            return Error.Rule("Ar.NotADocument", $"{item.DocumentNumber} is a {item.ItemType}, not an invoice or credit note.");
        }

        if (item.JournalEntryId is not { } journalId)
        {
            return Error.Rule("Ar.NoJournal", $"{item.DocumentNumber} has no posted journal to print from.");
        }

        bool isCredit = item.ItemType == OpenItemType.CreditNote;

        Company company = await _db.Companies.FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        Customer? customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == item.TraderId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        List<JournalLine> lines = await _db.Set<JournalLine>().Where(l => l.JournalEntryId == journalId).OrderBy(l => l.Id).ToListAsync(ct).ConfigureAwait(false);

        List<string> codes = lines.Select(l => l.AccountCode).Distinct().ToList();
        Dictionary<string, LedgerAccount> accounts = await _db.Accounts
            .Where(a => a.CompanyId == companyId && codes.Contains(a.AccountCode))
            .ToDictionaryAsync(a => a.AccountCode, ct).ConfigureAwait(false);

        // On an invoice the charge legs are credits; on a credit note they are debits. Signed so that a
        // charge reads positive either way.
        decimal tax = 0m;
        List<InvoiceDocumentLine> documentLines = [];
        foreach (JournalLine line in lines)
        {
            accounts.TryGetValue(line.AccountCode, out LedgerAccount? account);
            ControlAccountType control = account?.ControlAccountType ?? ControlAccountType.None;
            if (control == ControlAccountType.AccountsReceivable)
            {
                continue;
            }

            decimal charge = isCredit ? line.TransactionDebit - line.TransactionCredit : line.TransactionCredit - line.TransactionDebit;
            if (control is ControlAccountType.VatOutput or ControlAccountType.VatInput || !string.IsNullOrEmpty(line.TaxCode))
            {
                tax += charge;
                continue;
            }

            if (charge == 0m)
            {
                continue;
            }

            string description = !string.IsNullOrWhiteSpace(line.Narrative) ? line.Narrative! : account?.Name ?? line.AccountCode;
            documentLines.Add(new InvoiceDocumentLine(description, charge));
        }

        return new InvoiceDocument(
            isCredit,
            company.Name,
            string.IsNullOrWhiteSpace(company.TaxNumber) ? null : company.TaxNumber,
            customer?.Code ?? string.Empty,
            customer?.Name ?? string.Empty,
            string.IsNullOrWhiteSpace(customer?.TaxRegistration) ? null : customer!.TaxRegistration,
            item.DocumentNumber,
            item.DocumentDate,
            item.DueDate,
            item.Currency.ToString(),
            documentLines,
            tax,
            item.OutstandingTransaction,
            item.Narrative);
    }

    /// <inheritdoc />
    public async Task<Result<string>> RenderTextAsync(long companyId, long openItemId, int width = InvoiceFormatter.DefaultWidth, CancellationToken ct = default)
    {
        Result<InvoiceDocument> document = await BuildAsync(companyId, openItemId, ct).ConfigureAwait(false);
        return document.IsFailure ? document.Error : InvoiceFormatter.ToPlainText(document.Value, width);
    }
}
