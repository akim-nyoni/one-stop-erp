using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>
/// Manages recurring purchase-invoice templates and the billing run, posting real AP invoices through the
/// purchase ledger so recurring costs balance and age exactly like any other supplier invoice.
/// </summary>
public sealed class RecurringPurchaseInvoiceService : IRecurringPurchaseInvoiceService
{
    private readonly One4AllDbContext _db;
    private readonly IPurchaseLedgerService _purchaseLedger;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="purchaseLedger">The purchase ledger (posts the invoices).</param>
    public RecurringPurchaseInvoiceService(One4AllDbContext db, IPurchaseLedgerService purchaseLedger)
    {
        _db = db;
        _purchaseLedger = purchaseLedger;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateAsync(long companyId, long supplierId, string reference, CurrencyCode currency, decimal netAmount, decimal taxAmount, string expenseAccountCode, string? taxAccountCode, int intervalMonths, DateOnly firstRunDate, string? narrative, CancellationToken ct = default)
    {
        if (netAmount <= 0m)
        {
            return Error.Rule("Ap.RecurringNonPositive", "The net amount must be positive.");
        }

        if (taxAmount < 0m)
        {
            return Error.Rule("Ap.RecurringNegativeTax", "The tax amount cannot be negative.");
        }

        if (intervalMonths <= 0)
        {
            return Error.Rule("Ap.RecurringInterval", "The billing interval must be a positive number of months.");
        }

        bool supplierExists = await _db.Suppliers.AnyAsync(s => s.Id == supplierId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (!supplierExists)
        {
            return Error.NotFound("Ap.SupplierNotFound", $"Supplier {supplierId} not found.");
        }

        RecurringPurchaseInvoice template = new(companyId, supplierId, reference, currency, netAmount, taxAmount, expenseAccountCode, taxAccountCode, intervalMonths, firstRunDate, narrative);
        _db.Set<RecurringPurchaseInvoice>().Add(template);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return template.Id;
    }

    /// <inheritdoc />
    public Task<Result<int>> GenerateDueAsync(long companyId, DateOnly asOf, CancellationToken ct = default)
        => _db.AtomicAsync(() => GenerateDueCoreAsync(companyId, asOf, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<int>> GenerateDueCoreAsync(long companyId, DateOnly asOf, CancellationToken ct)
    {
        List<RecurringPurchaseInvoice> due = await _db.Set<RecurringPurchaseInvoice>()
            .Where(t => t.CompanyId == companyId && t.IsActive && t.NextRunDate <= asOf)
            .OrderBy(t => t.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        int generated = 0;
        foreach (RecurringPurchaseInvoice template in due)
        {
            while (template.IsActive && template.NextRunDate <= asOf)
            {
                DateOnly runDate = template.NextRunDate;
                Result<TradingPostingResult> posted = await _purchaseLedger.PostInvoiceAsync(new PurchaseInvoiceRequest
                {
                    CompanyId = companyId,
                    SupplierId = template.SupplierId,
                    DocumentNumber = $"{template.Reference}-{runDate:yyyyMM}",
                    DocumentDate = runDate,
                    Currency = template.Currency,
                    NetAmount = template.NetAmount,
                    TaxAmount = template.TaxAmount,
                    ExpenseAccountCode = template.ExpenseAccountCode,
                    TaxAccountCode = template.TaxAccountCode,
                    Narrative = template.Narrative ?? $"Recurring cost {template.Reference}",
                }, ct).ConfigureAwait(false);

                if (posted.IsFailure)
                {
                    return posted.Error;
                }

                template.AdvanceRun();
                generated++;
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return generated;
    }

    /// <inheritdoc />
    public async Task<Result> SetActiveAsync(long companyId, long templateId, bool active, CancellationToken ct = default)
    {
        RecurringPurchaseInvoice? template = await _db.Set<RecurringPurchaseInvoice>().FirstOrDefaultAsync(t => t.Id == templateId && t.CompanyId == companyId, ct).ConfigureAwait(false);
        if (template is null)
        {
            return Result.Failure(Error.NotFound("Ap.RecurringNotFound", $"Recurring purchase invoice {templateId} not found."));
        }

        template.SetActive(active);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}
