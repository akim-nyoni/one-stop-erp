using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Trading;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>
/// Raises finance charges on overdue customer balances. It scans the AR ledger for unsettled debit open
/// items whose (grace-adjusted) due date has passed, accrues simple interest from the due date to the run
/// date at the given annual rate (actual/365), and posts one finance-charge invoice per customer through
/// the sales ledger, so the charge ages and collects like any other invoice. Only base-currency items are
/// charged (foreign-currency interest is out of scope).
/// </summary>
public sealed class FinanceChargeService : IFinanceChargeService
{
    private readonly One4AllDbContext _db;
    private readonly ISalesLedgerService _salesLedger;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="salesLedger">The sales-ledger posting service.</param>
    public FinanceChargeService(One4AllDbContext db, ISalesLedgerService salesLedger)
    {
        _db = db;
        _salesLedger = salesLedger;
    }

    /// <inheritdoc />
    public Task<Result<FinanceChargeRunResult>> RaiseChargesAsync(FinanceChargeRunRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => RaiseChargesCoreAsync(request, ct), ct);

    // One unit of work: every customer's charge and the marks on the items it covers commit together or not at all.
    private async Task<Result<FinanceChargeRunResult>> RaiseChargesCoreAsync(FinanceChargeRunRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.InterestIncomeAccountCode))
        {
            return Error.Validation("FinanceCharge.Account", "The interest-income account is required.");
        }

        if (request.AnnualRatePercent <= 0m)
        {
            return new FinanceChargeRunResult([], 0m); // nothing to charge at a zero/negative rate
        }

        Company company = await _db.Companies.FirstAsync(c => c.Id == request.CompanyId, ct).ConfigureAwait(false);

        // Finance charges earn interest only under the compounding rule (by default no interest on interest).
        List<OpenItem> arDebits = await _db.Set<OpenItem>()
            .Where(o => o.CompanyId == request.CompanyId
                && o.Ledger == TradingLedger.AccountsReceivable
                && o.Sign == LedgerSign.Debit
                && (request.Compound || o.ItemType != OpenItemType.Interest)
                && o.OutstandingBase > 0m)
            .ToListAsync(ct).ConfigureAwait(false);

        // Overdue past the grace period, in the base currency only.
        List<OpenItem> overdue = arDebits
            .Where(o => o.Currency == company.BaseCurrency && o.DueDate.AddDays(request.GraceDays) < request.AsAtDate)
            .ToList();

        // Exempt accounts: flagged customers, and indigent ratepayers when the run exempts them.
        HashSet<long> overdueCustomers = [.. overdue.Select(o => o.TraderId)];
        HashSet<long> exempt = [.. await _db.Customers.Where(c => c.CompanyId == request.CompanyId && c.InterestExempt && overdueCustomers.Contains(c.Id))
            .Select(c => c.Id).ToListAsync(ct).ConfigureAwait(false)];
        if (request.ExemptIndigent)
        {
            exempt.UnionWith(await (
                from r in _db.Set<IndigentRegistration>()
                join p in _db.Properties on r.PropertyId equals p.Id
                where r.CompanyId == request.CompanyId && r.Status == IndigentStatus.Active && overdueCustomers.Contains(p.RatepayerCustomerId)
                select p.RatepayerCustomerId).ToListAsync(ct).ConfigureAwait(false));
        }

        overdue = [.. overdue.Where(o => !exempt.Contains(o.TraderId))];

        // Unallocated receipts and credit notes stop interest on what they cover from their date.
        HashSet<long> chargeable = [.. overdue.Select(o => o.TraderId)];
        var creditRows = await _db.Set<OpenItem>()
            .Where(o => o.CompanyId == request.CompanyId
                && o.Ledger == TradingLedger.AccountsReceivable
                && o.Sign == LedgerSign.Credit
                && o.OutstandingBase > 0m
                && o.DocumentDate <= request.AsAtDate
                && chargeable.Contains(o.TraderId))
            .Select(o => new { o.TraderId, o.Currency, o.DocumentDate, o.OutstandingBase })
            .ToListAsync(ct).ConfigureAwait(false);
        ILookup<long, UnallocatedCredit> credits = creditRows
            .Where(c => c.Currency == company.BaseCurrency)
            .ToLookup(c => c.TraderId, c => new UnallocatedCredit(c.DocumentDate, c.OutstandingBase));

        List<CustomerFinanceCharge> charges = [];
        foreach (IGrouping<long, OpenItem> byCustomer in overdue.GroupBy(o => o.TraderId).OrderBy(g => g.Key))
        {
            int maxDays = 0;
            List<OpenItem> charged = [];
            List<ChargeableItem> items = [];
            foreach (OpenItem item in byCustomer)
            {
                maxDays = Math.Max(maxDays, request.AsAtDate.DayNumber - item.DueDate.DayNumber);

                // Charge only the days not already charged: from the later of the due date and the last run.
                DateOnly from = item.InterestChargedTo is { } last && last > item.DueDate ? last : item.DueDate;
                if (request.AsAtDate.DayNumber - from.DayNumber <= 0)
                {
                    continue;
                }

                items.Add(new ChargeableItem(item.DueDate, from, item.OutstandingBase));
                charged.Add(item);
            }

            CreditAdjustedInterest adjusted = FinanceCharge.InterestAfterCredits(items, credits[byCustomer.Key], request.AsAtDate, request.AnnualRatePercent);
            decimal overdueAmount = byCustomer.Sum(o => o.OutstandingBase) - (items.Sum(i => i.Outstanding) - adjusted.OverdueAfterCredits);
            decimal interest = decimal.Round(adjusted.Interest, 2, MidpointRounding.AwayFromZero);
            if (interest <= 0m || interest < request.MinimumCharge)
            {
                // Below the minimum: the days stay uncharged and roll into a later run.
                continue;
            }

            string documentNumber = $"{request.DocumentPrefix}-{byCustomer.Key}-{request.AsAtDate:yyyyMMdd}";
            Result<TradingPostingResult> posting = await _salesLedger.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = request.CompanyId,
                CustomerId = byCustomer.Key,
                DocumentNumber = documentNumber,
                DocumentDate = request.AsAtDate,
                Currency = company.BaseCurrency,
                NetAmount = interest,
                TaxAmount = 0m,
                SalesAccountCode = request.InterestIncomeAccountCode,
                Narrative = "Finance charge on overdue balance",
            }, ct).ConfigureAwait(false);

            if (posting.IsFailure)
            {
                return posting.Error;
            }

            foreach (OpenItem item in charged)
            {
                item.RecordInterestChargedTo(request.AsAtDate);
            }

            OpenItem chargeItem = await _db.Set<OpenItem>().FirstAsync(o => o.Id == posting.Value.OpenItemId, ct).ConfigureAwait(false);
            chargeItem.MarkAsInterestCharge();
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            charges.Add(new CustomerFinanceCharge(byCustomer.Key, overdueAmount, maxDays, interest, documentNumber));
        }

        return new FinanceChargeRunResult(charges, charges.Sum(c => c.Interest), exempt.Count);
    }
}

/// <summary>
/// Built-in task <c>ar.finance-charges</c>: raises interest on overdue customer accounts on the company's <see cref="FinanceChargePolicy"/> as at the
/// run date (schedule it monthly). Each open item is charged only for the days since it was last charged, so a re-run the same day charges nothing more.
/// </summary>
public sealed class FinanceChargesTask : Application.Abstractions.Scheduling.IScheduledTask
{
    private readonly One4AllDbContext _db;
    private readonly IFinanceChargeService _charges;

    /// <summary>Creates the task.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="charges">The finance-charge service.</param>
    public FinanceChargesTask(One4AllDbContext db, IFinanceChargeService charges)
    {
        _db = db;
        _charges = charges;
    }

    /// <inheritdoc />
    public string Key => "ar.finance-charges";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        FinanceChargePolicy? policy = await _db.Set<FinanceChargePolicy>().AsNoTracking().FirstOrDefaultAsync(p => p.CompanyId == companyId, ct).ConfigureAwait(false);
        if (policy is null)
        {
            return "No finance-charge policy is set (Sales & Customers › Interest on overdue accounts › Save as the monthly policy); nothing charged.";
        }

        Result<FinanceChargeRunResult> run = await _charges.RaiseChargesAsync(new FinanceChargeRunRequest
        {
            CompanyId = companyId, AsAtDate = DateOnly.FromDateTime(runAt.UtcDateTime), AnnualRatePercent = policy.AnnualRatePercent, GraceDays = policy.GraceDays,
            MinimumCharge = policy.MinimumCharge, InterestIncomeAccountCode = policy.InterestIncomeAccountCode, DocumentPrefix = policy.DocumentPrefix, Compound = policy.Compound,
            ExemptIndigent = policy.ExemptIndigent,
        }, ct).ConfigureAwait(false);
        if (run.IsFailure)
        {
            return run.Error;
        }

        return $"{run.Value.Charges.Count} customer(s) charged {run.Value.TotalInterest:N2} interest at {policy.AnnualRatePercent:0.##}%"
            + (run.Value.CustomersExempted > 0 ? $"; {run.Value.CustomersExempted} exempted." : ".");
    }
}
