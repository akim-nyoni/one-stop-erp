using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Trading;
using One4All.Core.Common;
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
    public async Task<Result<FinanceChargeRunResult>> RaiseChargesAsync(FinanceChargeRunRequest request, CancellationToken ct = default)
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

        List<OpenItem> arDebits = await _db.Set<OpenItem>()
            .Where(o => o.CompanyId == request.CompanyId
                && o.Ledger == TradingLedger.AccountsReceivable
                && o.Sign == LedgerSign.Debit
                && o.OutstandingBase > 0m)
            .ToListAsync(ct).ConfigureAwait(false);

        // Overdue past the grace period, in the base currency only.
        List<OpenItem> overdue = arDebits
            .Where(o => o.Currency == company.BaseCurrency && o.DueDate.AddDays(request.GraceDays) < request.AsAtDate)
            .ToList();

        List<CustomerFinanceCharge> charges = [];
        foreach (IGrouping<long, OpenItem> byCustomer in overdue.GroupBy(o => o.TraderId).OrderBy(g => g.Key))
        {
            decimal interest = 0m;
            decimal overdueAmount = 0m;
            int maxDays = 0;
            foreach (OpenItem item in byCustomer)
            {
                int daysOverdue = request.AsAtDate.DayNumber - item.DueDate.DayNumber;
                interest += FinanceCharge.Interest(item.OutstandingBase, daysOverdue, request.AnnualRatePercent);
                overdueAmount += item.OutstandingBase;
                maxDays = Math.Max(maxDays, daysOverdue);
            }

            interest = decimal.Round(interest, 2, MidpointRounding.AwayFromZero);
            if (interest <= 0m || interest < request.MinimumCharge)
            {
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

            charges.Add(new CustomerFinanceCharge(byCustomer.Key, overdueAmount, maxDays, interest, documentNumber));
        }

        return new FinanceChargeRunResult(charges, charges.Sum(c => c.Interest));
    }
}
