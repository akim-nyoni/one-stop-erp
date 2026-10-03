using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Trading;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>
/// Assesses customers for dunning: it scans the AR ledger for unsettled debit open items whose due date has
/// passed, groups them by customer, and assigns each a reminder level from the age of the oldest overdue
/// item. It reports the overdue total and oldest days so reminders can be raised; it never posts.
/// </summary>
public sealed class DunningService : IDunningService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public DunningService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<DunningNotice>>> AssessAsync(long companyId, DateOnly asAtDate, DunningStages stages, CancellationToken ct = default)
    {
        List<OpenItem> overdue = await _db.Set<OpenItem>()
            .Where(o => o.CompanyId == companyId
                && o.Ledger == TradingLedger.AccountsReceivable
                && o.Sign == LedgerSign.Debit
                && o.OutstandingBase > 0m
                && o.DueDate < asAtDate)
            .ToListAsync(ct).ConfigureAwait(false);

        Dictionary<long, string> customerNames = await _db.Customers
            .Where(c => c.CompanyId == companyId)
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct).ConfigureAwait(false);

        // A customer who has promised to pay, and whose promised date has not passed, is not chased meanwhile.
        HashSet<long> promised = [.. await _db.Set<PaymentPromise>()
            .Where(p => p.CompanyId == companyId && p.Status == PromiseStatus.Open && p.MadeOn <= asAtDate && p.PayBy >= asAtDate)
            .Select(p => p.CustomerId).ToListAsync(ct).ConfigureAwait(false)];
        overdue.RemoveAll(o => promised.Contains(o.TraderId));

        HashSet<long> chased = [.. overdue.Select(o => o.TraderId)];
        Dictionary<long, decimal> credits = await UnallocatedCreditsAsync(companyId, chased, asAtDate, ct).ConfigureAwait(false);

        List<DunningNotice> notices = [];
        foreach (IGrouping<long, OpenItem> byCustomer in overdue.GroupBy(o => o.TraderId))
        {
            // Payments and credit notes not yet allocated count against the oldest invoices first.
            NetOverdue net = Dunning.ApplyCredits(byCustomer.Select(o => new OverdueItem(o.DueDate, o.OutstandingBase)), credits.GetValueOrDefault(byCustomer.Key), asAtDate);
            int level = Dunning.LevelFor(net.OldestDaysOverdue, stages);
            if (level < 1 || net.Amount <= 0m)
            {
                continue;
            }

            notices.Add(new DunningNotice(
                byCustomer.Key,
                customerNames.GetValueOrDefault(byCustomer.Key, $"Customer {byCustomer.Key}"),
                level,
                Dunning.LabelFor(level),
                net.Amount,
                net.OldestDaysOverdue));
        }

        IReadOnlyList<DunningNotice> ordered = notices
            .OrderByDescending(n => n.Level)
            .ThenBy(n => n.CustomerName, StringComparer.Ordinal)
            .ToList();
        return Result.Success(ordered);
    }

    /// <inheritdoc />
    public async Task<Result<string>> RenderReminderTextAsync(long companyId, long customerId, DateOnly asAtDate, DunningStages stages, int width = DunningLetterFormatter.DefaultWidth, CancellationToken ct = default)
    {
        string? customerName = await _db.Customers
            .Where(c => c.CompanyId == companyId && c.Id == customerId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (customerName is null)
        {
            return Error.NotFound("Dunning.CustomerNotFound", $"Customer {customerId} not found.");
        }

        List<OpenItem> overdue = await _db.Set<OpenItem>()
            .Where(o => o.CompanyId == companyId
                && o.Ledger == TradingLedger.AccountsReceivable
                && o.Sign == LedgerSign.Debit
                && o.TraderId == customerId
                && o.OutstandingBase > 0m
                && o.DueDate < asAtDate)
            .OrderBy(o => o.DueDate)
            .ToListAsync(ct).ConfigureAwait(false);

        if (overdue.Count == 0)
        {
            return Error.Rule("Dunning.NothingOverdue", $"{customerName} has nothing overdue as at {asAtDate:yyyy-MM-dd}.");
        }

        decimal credits = (await UnallocatedCreditsAsync(companyId, [customerId], asAtDate, ct).ConfigureAwait(false)).GetValueOrDefault(customerId);
        NetOverdue net = Dunning.ApplyCredits(overdue.Select(o => new OverdueItem(o.DueDate, o.OutstandingBase)), credits, asAtDate);
        if (net.Amount <= 0m)
        {
            return Error.Rule("Dunning.CoveredByCredits", $"{customerName}'s unallocated payments and credits cover everything overdue as at {asAtDate:yyyy-MM-dd}.");
        }

        int oldestDays = net.OldestDaysOverdue;
        int level = Dunning.LevelFor(oldestDays, stages);
        if (level < 1)
        {
            return Error.Rule("Dunning.NotDue", $"{customerName}'s oldest unpaid item is {oldestDays} day(s) overdue, inside the first reminder threshold.");
        }

        string companyName = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty;

        DunningReminder reminder = new()
        {
            CompanyName = companyName,
            CustomerName = customerName,
            AsAtDate = asAtDate,
            Level = level,
            LevelLabel = Dunning.LabelFor(level),
            Items = overdue
                .Select(o => new DunningReminderItem(o.DocumentNumber, o.DueDate, asAtDate.DayNumber - o.DueDate.DayNumber, o.OutstandingBase))
                .ToList(),
            UnallocatedCredits = net.CreditsApplied,
            TotalOverdue = net.Amount,
        };

        return DunningLetterFormatter.ToPlainText(reminder, width);
    }

    // Each customer's unallocated receipts and credit notes (AR credit items still outstanding), in base currency.
    private async Task<Dictionary<long, decimal>> UnallocatedCreditsAsync(long companyId, HashSet<long> customerIds, DateOnly asAtDate, CancellationToken ct)
        => await _db.Set<OpenItem>()
            .Where(o => o.CompanyId == companyId
                && o.Ledger == TradingLedger.AccountsReceivable
                && o.Sign == LedgerSign.Credit
                && o.OutstandingBase > 0m
                && o.DocumentDate <= asAtDate
                && customerIds.Contains(o.TraderId))
            .GroupBy(o => o.TraderId)
            .Select(g => new { CustomerId = g.Key, Total = g.Sum(o => o.OutstandingBase) })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Total, ct).ConfigureAwait(false);
}
