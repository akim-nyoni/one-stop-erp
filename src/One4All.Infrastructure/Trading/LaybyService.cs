using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>Lay-bye plans through the posting engine and the inventory service.</summary>
public sealed class LaybyService : ILaybyService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _engine;
    private readonly IInventoryService _inventory;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="engine">The posting engine.</param>
    /// <param name="inventory">The inventory service, which issues the goods on completion.</param>
    public LaybyService(One4AllDbContext db, IPostingEngine engine, IInventoryService inventory)
    {
        _db = db;
        _engine = engine;
        _inventory = inventory;
    }

    /// <inheritdoc />
    public Task<Result<long>> OpenAsync(LaybyRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => OpenCoreAsync(request, ct), ct);

    private async Task<Result<long>> OpenCoreAsync(LaybyRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Lines.Count == 0 || request.Lines.Any(l => l.Quantity <= 0m || l.UnitPrice < 0m) || request.FinalDate < request.OpenedOn
            || request.ForfeitPercent is < 0m or > 100m || string.IsNullOrWhiteSpace(request.PlanNumber))
        {
            return Error.Validation("Layby.Invalid", "A lay-bye needs a number, goods, a final date after it opens and a forfeiture of 0 to 100%.");
        }

        if (!await _db.Customers.AnyAsync(c => c.Id == request.CustomerId && c.CompanyId == request.CompanyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Layby.CustomerNotFound", $"Customer {request.CustomerId} not found.");
        }

        if (await _db.Set<LaybyPlan>().AnyAsync(p => p.CompanyId == request.CompanyId && p.PlanNumber == request.PlanNumber.Trim(), ct).ConfigureAwait(false))
        {
            return Error.Conflict("Layby.Duplicate", $"Lay-bye {request.PlanNumber} already exists.");
        }

        LaybyPlan plan = new(request.CompanyId, request.CustomerId, request.PlanNumber.Trim(), request.WarehouseId, request.OpenedOn, request.FinalDate, request.ForfeitPercent,
            request.DepositAccountCode.Trim(), request.RevenueAccountCode.Trim(), request.ForfeitureAccountCode.Trim());
        foreach (LaybyLineRequest line in request.Lines)
        {
            plan.AddLine(line.ItemId, line.Quantity, line.UnitPrice);
        }

        decimal minimum = decimal.Round(plan.Total * request.MinimumDepositPercent / 100m, 2, MidpointRounding.AwayFromZero);
        if (request.Deposit <= 0m || request.Deposit < minimum || request.Deposit >= plan.Total)
        {
            return Error.Rule("Layby.Deposit", $"The deposit must be at least {minimum:0.00} and less than the price of {plan.Total:0.00}.");
        }

        Result<PostingReceipt> posted = await PostAsync(plan, request.OpenedOn, $"Lay-bye {plan.PlanNumber} deposit",
            [(request.BankAccountCode, JournalSide.Debit, request.Deposit), (plan.DepositAccountCode, JournalSide.Credit, request.Deposit)], ct).ConfigureAwait(false);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        plan.Pay(request.Deposit, request.OpenedOn);
        _db.Set<LaybyPlan>().Add(plan);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return plan.Id;
    }

    /// <inheritdoc />
    public Task<Result<LaybyPayment>> PayAsync(long companyId, long planId, DateOnly date, decimal amount, string bankAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => PayCoreAsync(companyId, planId, date, amount, bankAccountCode, ct), ct);

    // One unit: the payment and, when it clears the balance, the sale and the stock issue.
    private async Task<Result<LaybyPayment>> PayCoreAsync(long companyId, long planId, DateOnly date, decimal amount, string bankAccountCode, CancellationToken ct)
    {
        Result<LaybyPlan> found = await OpenPlanAsync(companyId, planId, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        LaybyPlan plan = found.Value;
        if (amount <= 0m || amount > plan.Balance)
        {
            return Error.Rule("Layby.Payment", $"A payment must be positive and no more than the {plan.Balance:0.00} still owed.");
        }

        Result<PostingReceipt> posted = await PostAsync(plan, date, $"Lay-bye {plan.PlanNumber} instalment",
            [(bankAccountCode, JournalSide.Debit, amount), (plan.DepositAccountCode, JournalSide.Credit, amount)], ct).ConfigureAwait(false);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        bool completed = plan.Pay(amount, date);
        if (completed)
        {
            // The sale is made now: the payments held become revenue, and the goods leave stock.
            Result<PostingReceipt> sale = await PostAsync(plan, date, $"Lay-bye {plan.PlanNumber} completed",
                [(plan.DepositAccountCode, JournalSide.Debit, plan.Total), (plan.RevenueAccountCode, JournalSide.Credit, plan.Total)], ct).ConfigureAwait(false);
            if (sale.IsFailure)
            {
                return sale.Error;
            }

            foreach (LaybyLine line in plan.Lines)
            {
                // A kit leaves stock as its components.
                List<(Core.Inventory.Item Item, decimal Quantity)> stock = await Inventory.KitExplosion.StockLinesAsync(_db, companyId, line.ItemId, line.Quantity, ct).ConfigureAwait(false);
                Core.Inventory.Item? sold = await _db.Set<Core.Inventory.Item>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == line.ItemId, ct).ConfigureAwait(false);
                foreach ((long itemId, decimal quantity) in sold is { ItemType: Core.Inventory.ItemType.Kit } ? stock.Select(s => (s.Item.Id, s.Quantity)) : new[] { (line.ItemId, line.Quantity) })
                {
                    Result<StockMovementResult> issued = await _inventory.IssueAsync(new IssueStockRequest
                    {
                        CompanyId = companyId, ItemId = itemId, WarehouseId = plan.WarehouseId, Date = date, Quantity = quantity, Reference = plan.PlanNumber,
                    }, ct).ConfigureAwait(false);
                    if (issued.IsFailure)
                    {
                        return issued.Error;
                    }
                }
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new LaybyPayment(plan.Paid, plan.Balance, completed);
    }

    /// <inheritdoc />
    public Task<Result<LaybyCancellation>> CancelAsync(long companyId, long planId, DateOnly date, string refundAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => CancelCoreAsync(companyId, planId, date, refundAccountCode, ct), ct);

    private async Task<Result<LaybyCancellation>> CancelCoreAsync(long companyId, long planId, DateOnly date, string refundAccountCode, CancellationToken ct)
    {
        Result<LaybyPlan> found = await OpenPlanAsync(companyId, planId, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        LaybyPlan plan = found.Value;
        decimal forfeited = plan.Cancel(date);
        decimal refund = plan.Paid - forfeited;
        List<(string Account, JournalSide Side, decimal Amount)> lines = [(plan.DepositAccountCode, JournalSide.Debit, plan.Paid)];
        if (forfeited > 0m)
        {
            lines.Add((plan.ForfeitureAccountCode, JournalSide.Credit, forfeited));
        }

        if (refund > 0m)
        {
            lines.Add((refundAccountCode, JournalSide.Credit, refund));
        }

        Result<PostingReceipt> posted = await PostAsync(plan, date, $"Lay-bye {plan.PlanNumber} cancelled", lines, ct).ConfigureAwait(false);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new LaybyCancellation(plan.PlanNumber, plan.Paid, forfeited, refund);
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<LaybyCancellation>>> DefaultOverdueAsync(long companyId, DateOnly asAtDate, string refundAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => DefaultOverdueCoreAsync(companyId, asAtDate, refundAccountCode, ct), ct);

    private async Task<Result<IReadOnlyList<LaybyCancellation>>> DefaultOverdueCoreAsync(long companyId, DateOnly asAtDate, string refundAccountCode, CancellationToken ct)
    {
        List<long> overdue = await _db.Set<LaybyPlan>()
            .Where(p => p.CompanyId == companyId && p.Status == LaybyStatus.Open && p.FinalDate < asAtDate)
            .OrderBy(p => p.FinalDate).ThenBy(p => p.Id).Select(p => p.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        List<LaybyCancellation> cancelled = [];
        foreach (long id in overdue)
        {
            Result<LaybyCancellation> result = await CancelCoreAsync(companyId, id, asAtDate, refundAccountCode, ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return result.Error;
            }

            cancelled.Add(result.Value);
        }

        return Result<IReadOnlyList<LaybyCancellation>>.Success(cancelled);
    }

    private async Task<Result<LaybyPlan>> OpenPlanAsync(long companyId, long planId, CancellationToken ct)
    {
        LaybyPlan? plan = await _db.Set<LaybyPlan>().Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == planId && p.CompanyId == companyId, ct).ConfigureAwait(false);
        if (plan is null)
        {
            return Error.NotFound("Layby.NotFound", $"Lay-bye {planId} not found.");
        }

        if (plan.Status != LaybyStatus.Open)
        {
            return Error.Rule("Layby.NotOpen", $"Lay-bye {plan.PlanNumber} is {plan.Status}.");
        }

        return plan;
    }

    private async Task<Result<PostingReceipt>> PostAsync(LaybyPlan plan, DateOnly date, string narrative, IReadOnlyList<(string Account, JournalSide Side, decimal Amount)> lines, CancellationToken ct)
    {
        CurrencyCode currency = await _db.Companies.Where(c => c.Id == plan.CompanyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false);
        return await _engine.PostAsync(new JournalRequest
        {
            CompanyId = plan.CompanyId,
            SourceModule = "AR",
            PostingDate = date,
            TransactionCurrency = currency,
            DocumentType = "Layby",
            DocumentNumber = plan.PlanNumber,
            Narrative = narrative,
            Lines = [.. lines.Select(l => new JournalLineRequest { AccountCode = l.Account, Side = l.Side, TransactionAmount = l.Amount })],
        }, ct).ConfigureAwait(false);
    }
}
