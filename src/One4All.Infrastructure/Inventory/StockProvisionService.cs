using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Inventory;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Inventory;

/// <summary>Assesses and books stock provisions (see <see cref="IStockProvisionService"/>).</summary>
public sealed class StockProvisionService : IStockProvisionService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The posting engine.</param>
    public StockProvisionService(One4AllDbContext db, IPostingEngine posting)
    {
        _db = db;
        _posting = posting;
    }

    /// <inheritdoc />
    public async Task<Result<StockProvisionReport>> AssessAsync(long companyId, DateOnly asAtDate, StockProvisionPolicy policy, string provisionAccountCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(provisionAccountCode))
        {
            return Error.Validation("Provision.Account", "The provision account is required.");
        }

        Company? company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Provision.CompanyNotFound", $"Company {companyId} not found.");
        }

        // Holdings as at the date, from the movements (never the current balances).
        var holdings = await _db.StockMovements
            .Where(m => m.CompanyId == companyId && m.Date <= asAtDate)
            .GroupBy(m => new { m.ItemId, m.WarehouseId })
            .Select(g => new
            {
                g.Key.ItemId,
                g.Key.WarehouseId,
                Quantity = g.Sum(m => m.Quantity),
                Cost = g.Sum(m => m.Value),
                FirstDate = g.Min(m => m.Date),
                LastIssue = g.Where(m => m.Type == StockMovementType.Issue).Max(m => (DateOnly?)m.Date),
            })
            .Where(h => h.Quantity > 0m)
            .ToListAsync(ct).ConfigureAwait(false);

        List<long> itemIds = [.. holdings.Select(h => h.ItemId).Distinct()];
        List<long> warehouseIds = [.. holdings.Select(h => h.WarehouseId).Distinct()];
        var items = await _db.Items.IgnoreQueryFilters().Where(i => itemIds.Contains(i.Id)).Select(i => new { i.Id, i.Code, i.Description }).ToDictionaryAsync(i => i.Id, ct).ConfigureAwait(false);
        var warehouses = await _db.Warehouses.IgnoreQueryFilters().Where(w => warehouseIds.Contains(w.Id)).Select(w => new { w.Id, w.Code }).ToDictionaryAsync(w => w.Id, ct).ConfigureAwait(false);
        Dictionary<long, decimal> prices = await SellingPricesAsync(companyId, company.BaseCurrency, itemIds, ct).ConfigureAwait(false);

        List<StockProvisionLine> lines = [];
        foreach (var h in holdings.OrderBy(h => items.GetValueOrDefault(h.ItemId)?.Code, StringComparer.Ordinal).ThenBy(h => h.WarehouseId))
        {
            int days = asAtDate.DayNumber - (h.LastIssue ?? h.FirstDate).DayNumber;
            decimal? price = prices.TryGetValue(h.ItemId, out decimal p) ? p : null;
            decimal cost = decimal.Round(h.Cost, 2, MidpointRounding.AwayFromZero);
            StockProvisionResult result = StockProvision.Assess(new StockHolding(h.Quantity, cost, price, days), policy);
            var item = items.GetValueOrDefault(h.ItemId);
            lines.Add(new StockProvisionLine(h.ItemId, item?.Code ?? $"#{h.ItemId}", item?.Description ?? string.Empty, h.WarehouseId,
                warehouses.GetValueOrDefault(h.WarehouseId)?.Code ?? $"#{h.WarehouseId}", h.Quantity, cost, price, result.NetRealisableValue, days, result.Provision, result.Basis));
        }

        decimal booked = await BookedAsync(companyId, provisionAccountCode.Trim(), asAtDate, ct).ConfigureAwait(false);
        return new StockProvisionReport(asAtDate, lines, lines.Sum(l => l.Provision), booked);
    }

    /// <inheritdoc />
    public Task<Result<StockProvisionPostingResult>> PostAsync(long companyId, DateOnly asAtDate, StockProvisionPolicy policy, string provisionAccountCode, string writeDownExpenseAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => PostCoreAsync(companyId, asAtDate, policy, provisionAccountCode, writeDownExpenseAccountCode, ct), ct);

    // One unit of work: the assessment and its adjusting journal.
    private async Task<Result<StockProvisionPostingResult>> PostCoreAsync(long companyId, DateOnly asAtDate, StockProvisionPolicy policy, string provisionAccountCode, string writeDownExpenseAccountCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(writeDownExpenseAccountCode))
        {
            return Error.Validation("Provision.ExpenseAccount", "The write-down expense account is required.");
        }

        Result<StockProvisionReport> assessed = await AssessAsync(companyId, asAtDate, policy, provisionAccountCode, ct).ConfigureAwait(false);
        if (assessed.IsFailure)
        {
            return assessed.Error;
        }

        StockProvisionReport report = assessed.Value;
        decimal adjustment = report.Adjustment;
        if (adjustment == 0m)
        {
            return new StockProvisionPostingResult(report, null);
        }

        // A growing provision charges the write-down expense; a shrinking one releases it.
        bool charge = adjustment > 0m;
        decimal amount = Math.Abs(adjustment);
        Company company = await _db.Companies.FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "INV",
            PostingDate = asAtDate,
            TransactionCurrency = company.BaseCurrency,
            EnforceManualJournalRules = false,
            DocumentType = "StockProvision",
            DocumentNumber = $"PROV-{asAtDate:yyyyMMdd}",
            Narrative = charge ? $"Stock provision raised to {report.TargetProvision:N2}" : $"Stock provision released to {report.TargetProvision:N2}",
            Lines =
            [
                new JournalLineRequest { AccountCode = writeDownExpenseAccountCode.Trim(), Side = charge ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = provisionAccountCode.Trim(), Side = charge ? JournalSide.Credit : JournalSide.Debit, TransactionAmount = amount },
            ],
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        return new StockProvisionPostingResult(report, posted.Value.JournalNumber);
    }

    // The provision already on the contra-stock account at the date (credit balance).
    private async Task<decimal> BookedAsync(long companyId, string provisionAccountCode, DateOnly asAtDate, CancellationToken ct)
    {
        var totals = await (
            from l in _db.Set<JournalLine>()
            join j in _db.Journals on l.JournalEntryId equals j.Id
            where j.CompanyId == companyId && j.Status == JournalStatus.Posted && j.PostingDate <= asAtDate && l.AccountCode == provisionAccountCode
            select new { l.BaseDebit, l.BaseCredit })
            .ToListAsync(ct).ConfigureAwait(false);
        return totals.Sum(t => t.BaseCredit) - totals.Sum(t => t.BaseDebit);
    }

    // The default price list's base-currency price for each item (its lowest quantity break).
    private async Task<Dictionary<long, decimal>> SellingPricesAsync(long companyId, CurrencyCode baseCurrency, List<long> itemIds, CancellationToken ct)
    {
        PriceList? list = await _db.PriceLists.Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.IsDefault && p.IsActive && p.Currency == baseCurrency, ct).ConfigureAwait(false);
        if (list is null)
        {
            return [];
        }

        return list.Lines
            .Where(l => itemIds.Contains(l.ItemId))
            .GroupBy(l => l.ItemId)
            .ToDictionary(g => g.Key, g => g.OrderBy(l => l.MinimumQuantity).First().UnitPrice);
    }
}
