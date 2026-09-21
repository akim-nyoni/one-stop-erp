using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Pos;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using One4All.Licensing.Core;

namespace One4All.Infrastructure.Pos;

/// <summary>Point of Sale service (brief §4.1): till sessions, sales with multi-currency tendering, and X/Z reads.</summary>
public sealed class PosService : IPosService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _postingEngine;
    private readonly IInventoryService _inventory;
    private readonly IClock _clock;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="postingEngine">The posting engine.</param>
    /// <param name="inventory">The inventory service.</param>
    /// <param name="clock">The clock.</param>
    public PosService(One4AllDbContext db, IPostingEngine postingEngine, IInventoryService inventory, IClock clock)
    {
        _db = db;
        _postingEngine = postingEngine;
        _inventory = inventory;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<long>> OpenTillAsync(long companyId, long terminalId, long cashierUserId, decimal openingFloat, CancellationToken ct = default)
    {
        TillSession session = new(companyId, terminalId, cashierUserId, _clock.UtcNow, openingFloat);
        _db.Set<TillSession>().Add(session);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return session.Id;
    }

    /// <inheritdoc />
    public async Task<Result<CompleteSaleResult>> CompleteSaleAsync(CompleteSaleRequest request, CancellationToken ct = default)
    {
        Company company = await _db.Companies.FirstAsync(c => c.Id == request.CompanyId, ct).ConfigureAwait(false);
        CurrencyCode baseCurrency = company.BaseCurrency;

        Sale sale = new(request.CompanyId, request.TerminalId, request.TillSessionId, request.CashierUserId, request.SaleNumber, request.SaleDate, baseCurrency);

        foreach (PosSaleLineRequest line in request.Lines)
        {
            sale.AddLine(new SaleLine(line.ItemId, line.Description, request.WarehouseId, line.Quantity, line.UnitPrice, line.LineNet, line.TaxAmount, line.RevenueAccountCode));
        }

        List<JournalLineRequest> journalLines = [];
        foreach (PosTenderRequest tender in request.Tenders)
        {
            Result<decimal> rateResult = await ResolveRateAsync(baseCurrency, tender.Currency, tender.ExchangeRate, request.SaleDate, ct).ConfigureAwait(false);
            if (rateResult.IsFailure)
            {
                return rateResult.Error;
            }

            decimal rate = rateResult.Value;
            decimal baseAmount = decimal.Round(tender.Amount * rate, 4, MidpointRounding.ToEven);
            sale.AddTender(new SaleTender(tender.TenderType, tender.Currency, tender.Amount, rate, baseAmount, tender.AccountCode, tender.IsChange));

            journalLines.Add(new JournalLineRequest { AccountCode = tender.AccountCode, Side = tender.IsChange ? JournalSide.Credit : JournalSide.Debit, TransactionAmount = baseAmount });
        }

        // Revenue (net) per line, then output tax.
        foreach (SaleLine line in sale.Lines)
        {
            journalLines.Add(new JournalLineRequest { AccountCode = line.RevenueAccountCode, Side = JournalSide.Credit, TransactionAmount = line.LineNet });
        }

        if (sale.TotalTax != 0m && request.TaxAccountCode is not null)
        {
            journalLines.Add(new JournalLineRequest { AccountCode = request.TaxAccountCode, Side = JournalSide.Credit, TransactionAmount = sale.TotalTax });
        }

        if (Math.Abs(sale.NetTenderedBase - sale.TotalGross) > 0.0001m)
        {
            return Error.Rule("Pos.Unbalanced", $"Tenders {sale.NetTenderedBase:0.00} do not equal the sale total {sale.TotalGross:0.00}.");
        }

        Result<PostingReceipt> posting = await _postingEngine.PostAsync(new JournalRequest
        {
            CompanyId = request.CompanyId,
            SourceModule = "POS",
            PostingDate = request.SaleDate,
            TransactionCurrency = baseCurrency,
            EnforceManualJournalRules = false,
            DocumentType = "PosSale",
            DocumentNumber = request.SaleNumber,
            Narrative = $"POS sale {request.SaleNumber}",
            Lines = journalLines,
        }, ct).ConfigureAwait(false);

        if (posting.IsFailure)
        {
            return posting.Error;
        }

        sale.JournalEntryId = posting.Value.JournalId;
        _db.Set<Sale>().Add(sale);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Cost of sales: issue stock for stock-tracked lines.
        foreach (SaleLine line in sale.Lines)
        {
            Item? item = await _db.Set<Item>().FirstOrDefaultAsync(i => i.Id == line.ItemId, ct).ConfigureAwait(false);
            if (item is { IsStockTracked: true } && !string.IsNullOrEmpty(item.CostOfSalesAccountCode))
            {
                Result<StockMovementResult> issue = await _inventory.IssueAsync(new IssueStockRequest
                {
                    CompanyId = request.CompanyId,
                    ItemId = line.ItemId,
                    WarehouseId = request.WarehouseId,
                    Date = request.SaleDate,
                    Quantity = line.Quantity,
                    Reference = request.SaleNumber,
                }, ct).ConfigureAwait(false);

                if (issue.IsFailure)
                {
                    return issue.Error;
                }
            }
        }

        return new CompleteSaleResult(sale.Id, posting.Value.JournalNumber, sale.TotalGross);
    }

    /// <inheritdoc />
    public async Task<Result<string>> VoidSaleAsync(long companyId, long saleId, DateOnly date, string reason, string? taxAccountCode = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("Pos.ReasonRequired", "Voiding a sale requires a reason.");
        }

        Sale? sale = await _db.Set<Sale>().Include(s => s.Lines).Include(s => s.Tenders)
            .FirstOrDefaultAsync(s => s.Id == saleId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (sale is null)
        {
            return Error.NotFound("Pos.SaleNotFound", $"Sale {saleId} not found.");
        }

        if (sale.Status != SaleStatus.Completed)
        {
            return Error.Rule("Pos.SaleNotCompleted", "Only a completed sale can be voided.");
        }

        if (sale.TotalTax != 0m && string.IsNullOrWhiteSpace(taxAccountCode))
        {
            return Error.Validation("Pos.TaxAccountRequired", "Voiding a sale with tax needs the output-tax account.");
        }

        // Inverse of the sale journal: refund tenders (Cr), unwind revenue and tax (Dr).
        List<JournalLineRequest> lines = [];
        foreach (SaleTender tender in sale.Tenders)
        {
            lines.Add(new JournalLineRequest { AccountCode = tender.AccountCode, Side = tender.IsChange ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = tender.BaseAmount });
        }

        foreach (SaleLine line in sale.Lines)
        {
            lines.Add(new JournalLineRequest { AccountCode = line.RevenueAccountCode, Side = JournalSide.Debit, TransactionAmount = line.LineNet });
        }

        if (sale.TotalTax != 0m)
        {
            lines.Add(new JournalLineRequest { AccountCode = taxAccountCode!, Side = JournalSide.Debit, TransactionAmount = sale.TotalTax });
        }

        Result<PostingReceipt> posting = await _postingEngine.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "POS",
            PostingDate = date,
            TransactionCurrency = sale.BaseCurrency,
            EnforceManualJournalRules = false,
            DocumentType = "PosSaleVoid",
            DocumentNumber = sale.SaleNumber,
            Narrative = $"Void POS sale {sale.SaleNumber}: {reason}",
            Lines = lines,
        }, ct).ConfigureAwait(false);

        if (posting.IsFailure)
        {
            return posting.Error;
        }

        // Reverse the cost-of-sales stock issues raised by the sale.
        List<StockMovement> issues = await _db.Set<StockMovement>()
            .Where(m => m.CompanyId == companyId && m.Reference == sale.SaleNumber && m.Type == StockMovementType.Issue && !m.IsReversed)
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (StockMovement issue in issues)
        {
            Result<StockMovementResult> reversed = await _inventory.ReverseIssueAsync(companyId, issue.Id, date, $"VOID:{sale.SaleNumber}", ct).ConfigureAwait(false);
            if (reversed.IsFailure)
            {
                return reversed.Error;
            }
        }

        sale.Void();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return posting.Value.JournalNumber;
    }

    /// <inheritdoc />
    public Task<Result<TillRead>> XReadAsync(long tillSessionId, CancellationToken ct = default) => ReadAsync(tillSessionId, close: false, ct);

    /// <inheritdoc />
    public Task<Result<TillRead>> CloseTillAsync(long tillSessionId, CancellationToken ct = default) => ReadAsync(tillSessionId, close: true, ct);

    private async Task<Result<TillRead>> ReadAsync(long tillSessionId, bool close, CancellationToken ct)
    {
        TillSession? session = await _db.Set<TillSession>().FirstOrDefaultAsync(s => s.Id == tillSessionId, ct).ConfigureAwait(false);
        if (session is null)
        {
            return Error.NotFound("Pos.SessionNotFound", $"Till session {tillSessionId} not found.");
        }

        List<Sale> sales = await _db.Set<Sale>()
            .Include(s => s.Lines).Include(s => s.Tenders)
            .Where(s => s.TillSessionId == tillSessionId && s.Status == SaleStatus.Completed)
            .ToListAsync(ct).ConfigureAwait(false);

        decimal gross = sales.Sum(s => s.TotalGross);
        Dictionary<TenderType, decimal> tenderTotals = sales
            .SelectMany(s => s.Tenders)
            .Where(t => !t.IsChange)
            .GroupBy(t => t.TenderType)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.BaseAmount));

        int? zNumber = session.ZReadNumber;
        if (close && session.Status == TillSessionStatus.Open)
        {
            int? currentMax = await _db.Set<TillSession>()
                .Where(s => s.TerminalId == session.TerminalId && s.ZReadNumber != null)
                .MaxAsync(s => (int?)s.ZReadNumber, ct).ConfigureAwait(false);
            int nextZ = (currentMax ?? 0) + 1;
            session.Close(_clock.UtcNow, nextZ);
            zNumber = nextZ;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return new TillRead(sales.Count, gross, tenderTotals, zNumber);
    }

    private async Task<Result<decimal>> ResolveRateAsync(CurrencyCode baseCurrency, CurrencyCode tenderCurrency, decimal? explicitRate, DateOnly date, CancellationToken ct)
    {
        if (tenderCurrency == baseCurrency)
        {
            return 1m;
        }

        if (explicitRate is { } r and > 0m)
        {
            return r;
        }

        ExchangeRate? rate = await _db.ExchangeRates
            .Where(x => x.FromCurrency == tenderCurrency && x.ToCurrency == baseCurrency && x.RateType == ExchangeRateType.Daily && x.EffectiveFrom <= date)
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        if (rate is null)
        {
            return Error.Rule("Pos.NoRate", $"No daily rate from {tenderCurrency} to {baseCurrency} on or before {date:yyyy-MM-dd}.");
        }

        return rate.Quotation == QuotationType.Direct ? rate.Rate : 1m / rate.Rate;
    }
}
