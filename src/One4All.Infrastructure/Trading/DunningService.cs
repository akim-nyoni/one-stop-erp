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

        List<DunningNotice> notices = [];
        foreach (IGrouping<long, OpenItem> byCustomer in overdue.GroupBy(o => o.TraderId))
        {
            int oldestDays = byCustomer.Max(o => asAtDate.DayNumber - o.DueDate.DayNumber);
            int level = Dunning.LevelFor(oldestDays, stages);
            if (level < 1)
            {
                continue;
            }

            notices.Add(new DunningNotice(
                byCustomer.Key,
                customerNames.GetValueOrDefault(byCustomer.Key, $"Customer {byCustomer.Key}"),
                level,
                Dunning.LabelFor(level),
                byCustomer.Sum(o => o.OutstandingBase),
                oldestDays));
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

        int oldestDays = overdue.Max(o => asAtDate.DayNumber - o.DueDate.DayNumber);
        int level = Dunning.LevelFor(oldestDays, stages);
        if (level < 1)
        {
            return Error.Rule("Dunning.NotDue", $"{customerName}'s oldest item is {oldestDays} day(s) overdue, inside the first reminder threshold.");
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
            TotalOverdue = overdue.Sum(o => o.OutstandingBase),
        };

        return DunningLetterFormatter.ToPlainText(reminder, width);
    }
}
