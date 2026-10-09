using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Abstractions.Scheduling;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Communications;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Trading;

namespace One4All.Infrastructure.Communications;

/// <summary>Queues customer statements for email in one despatch batch.</summary>
public sealed class StatementDespatchService : IStatementDespatchService
{
    private readonly One4AllDbContext _db;
    private readonly IStatementService _statements;
    private readonly INotificationService _messages;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="statements">The statement renderer.</param>
    /// <param name="messages">The notification service.</param>
    public StatementDespatchService(One4AllDbContext db, IStatementService statements, INotificationService messages)
    {
        _db = db;
        _statements = statements;
        _messages = messages;
    }

    /// <inheritdoc />
    public Task<Result<StatementDespatch>> QueueAsync(long companyId, DateOnly asAt, string templateCode, string language, decimal minimumBalance, CancellationToken ct = default)
        => QueueCoreAsync(companyId, asAt, templateCode, language, minimumBalance, null, $"Statements {asAt:yyyy-MM-dd}", ct);

    /// <inheritdoc />
    public Task<Result<StatementDespatch>> QueueAsync(long companyId, DateOnly asAt, string templateCode, string language, decimal minimumBalance, IReadOnlyCollection<long> customerIds, string title,
        CancellationToken ct = default)
        => QueueCoreAsync(companyId, asAt, templateCode, language, minimumBalance, customerIds, title, ct);

    private async Task<Result<StatementDespatch>> QueueCoreAsync(long companyId, DateOnly asAt, string templateCode, string language, decimal minimumBalance, IReadOnlyCollection<long>? only, string title,
        CancellationToken ct)
    {
        // Balances as at the date: original amounts less allocations made by then, net of credits.
        Dictionary<long, decimal> balances = (await OpenItemsAsAt.LoadAsync(_db, companyId, TradingLedger.AccountsReceivable, asAt, ct).ConfigureAwait(false))
            .GroupBy(i => i.Item.TraderId).ToDictionary(g => g.Key, g => g.Sum(i => i.Signed(LedgerSign.Debit)));
        List<long> owing = [.. balances.Where(b => b.Value > 0m && (only is null || only.Contains(b.Key))).Select(b => b.Key)];
        List<Customer> customers = await _db.Customers.AsNoTracking().Where(c => c.CompanyId == companyId && owing.Contains(c.Id)).OrderBy(c => c.Code).ToListAsync(ct).ConfigureAwait(false);

        List<DespatchRecipient> recipients = [];
        int noEmail = 0, belowMinimum = 0;
        foreach (Customer customer in customers)
        {
            decimal balance = balances[customer.Id];
            if (balance < minimumBalance)
            {
                belowMinimum++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(customer.Email))
            {
                noEmail++;
                continue;
            }

            Result<string> statement = await _statements.RenderCustomerStatementTextAsync(companyId, customer.Id, asAt, ct: ct).ConfigureAwait(false);
            if (statement.IsFailure)
            {
                return statement.Error;
            }

            recipients.Add(new DespatchRecipient(customer.Email.Trim(), new Dictionary<string, string>
            {
                ["Name"] = customer.Name,
                ["Account"] = customer.Code,
                ["Balance"] = balance.ToString("N2", CultureInfo.InvariantCulture),
                ["Currency"] = customer.Currency.ToString(),
                ["AsAt"] = asAt.ToString("d MMMM yyyy", CultureInfo.InvariantCulture),
                ["Statement"] = statement.Value,
            }));
        }

        if (recipients.Count == 0)
        {
            return new StatementDespatch(null, 0, noEmail, belowMinimum);
        }

        Result<long> batch = await _messages.QueueAsync(companyId, title, templateCode, language, MessageChannel.Email, recipients, ct).ConfigureAwait(false);
        return batch.IsFailure ? batch.Error : new StatementDespatch(batch.Value, recipients.Count, noEmail, belowMinimum);
    }
}

/// <summary>Built-in task: queue last month's statements by email (template STATEMENT, English, balances of at least 1).</summary>
public sealed class EmailStatementsTask : IScheduledTask
{
    private readonly IStatementDespatchService _despatch;

    /// <summary>Creates the task.</summary>
    /// <param name="despatch">The statement despatch.</param>
    public EmailStatementsTask(IStatementDespatchService despatch) => _despatch = despatch;

    /// <inheritdoc />
    public string Key => "statements.email";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        DateOnly today = DateOnly.FromDateTime(runAt.UtcDateTime);
        DateOnly lastMonthEnd = new DateOnly(today.Year, today.Month, 1).AddDays(-1);
        Result<StatementDespatch> queued = await _despatch.QueueAsync(companyId, lastMonthEnd, "STATEMENT", "en", 1m, ct).ConfigureAwait(false);
        return queued.IsFailure
            ? queued.Error
            : $"{queued.Value.Queued} statements to {lastMonthEnd:yyyy-MM-dd} queued; {queued.Value.NoEmail} customers without email.";
    }
}
