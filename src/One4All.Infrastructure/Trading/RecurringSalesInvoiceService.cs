using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>
/// Manages recurring sales-invoice templates and the billing run, posting real AR invoices through the sales
/// ledger so recurring billing balances and ages exactly like any other invoice.
/// </summary>
public sealed class RecurringSalesInvoiceService : IRecurringSalesInvoiceService
{
    private readonly One4AllDbContext _db;
    private readonly ISalesLedgerService _salesLedger;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="salesLedger">The sales ledger (posts the invoices).</param>
    public RecurringSalesInvoiceService(One4AllDbContext db, ISalesLedgerService salesLedger)
    {
        _db = db;
        _salesLedger = salesLedger;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateAsync(long companyId, long customerId, string reference, CurrencyCode currency, decimal netAmount, decimal taxAmount, string salesAccountCode, string? taxAccountCode, int intervalMonths, DateOnly firstRunDate, string? narrative, CancellationToken ct = default)
    {
        if (netAmount <= 0m)
        {
            return Error.Rule("Ar.RecurringNonPositive", "The net amount must be positive.");
        }

        if (taxAmount < 0m)
        {
            return Error.Rule("Ar.RecurringNegativeTax", "The tax amount cannot be negative.");
        }

        if (intervalMonths <= 0)
        {
            return Error.Rule("Ar.RecurringInterval", "The billing interval must be a positive number of months.");
        }

        bool customerExists = await _db.Customers.AnyAsync(c => c.Id == customerId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (!customerExists)
        {
            return Error.NotFound("Ar.CustomerNotFound", $"Customer {customerId} not found.");
        }

        RecurringSalesInvoice template = new(companyId, customerId, reference, currency, netAmount, taxAmount, salesAccountCode, taxAccountCode, intervalMonths, firstRunDate, narrative);
        _db.Set<RecurringSalesInvoice>().Add(template);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return template.Id;
    }

    /// <inheritdoc />
    public Task<Result<int>> GenerateDueAsync(long companyId, DateOnly asOf, CancellationToken ct = default)
        => _db.AtomicAsync(() => GenerateDueCoreAsync(companyId, asOf, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<int>> GenerateDueCoreAsync(long companyId, DateOnly asOf, CancellationToken ct)
    {
        List<RecurringSalesInvoice> due = await _db.Set<RecurringSalesInvoice>()
            .Where(t => t.CompanyId == companyId && t.IsActive && t.NextRunDate <= asOf)
            .OrderBy(t => t.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        int generated = 0;
        foreach (RecurringSalesInvoice template in due)
        {
            // Catch up every period that has fallen due on or before the cut-off.
            while (template.IsActive && template.NextRunDate <= asOf)
            {
                DateOnly runDate = template.NextRunDate;
                Result<TradingPostingResult> posted = await _salesLedger.PostInvoiceAsync(new SalesInvoiceRequest
                {
                    CompanyId = companyId,
                    CustomerId = template.CustomerId,
                    DocumentNumber = $"{template.Reference}-{runDate:yyyyMM}",
                    DocumentDate = runDate,
                    Currency = template.Currency,
                    NetAmount = template.NetAmount,
                    TaxAmount = template.TaxAmount,
                    SalesAccountCode = template.SalesAccountCode,
                    TaxAccountCode = template.TaxAccountCode,
                    Narrative = template.Narrative ?? $"Recurring invoice {template.Reference}",
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
        RecurringSalesInvoice? template = await _db.Set<RecurringSalesInvoice>().FirstOrDefaultAsync(t => t.Id == templateId && t.CompanyId == companyId, ct).ConfigureAwait(false);
        if (template is null)
        {
            return Result.Failure(Error.NotFound("Ar.RecurringNotFound", $"Recurring invoice {templateId} not found."));
        }

        template.SetActive(active);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}
