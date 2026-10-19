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
    public DunningService(One4AllDbContext db, Application.Abstractions.Communications.INotificationService? messages = null)
    {
        _db = db;
        _messages = messages;
    }

    private readonly Application.Abstractions.Communications.INotificationService? _messages;

    /// <inheritdoc />
    public async Task<Result> SaveGroupAsync(long companyId, DunningGroupInput group, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        string code = (group.Code ?? string.Empty).Trim().ToUpperInvariant();
        DunningStages s = group.Stages;
        if (code.Length == 0 || string.IsNullOrWhiteSpace(group.Name) || (!group.IsExempt && (s.FirstReminderDays < 1 || s.SecondReminderDays <= s.FirstReminderDays || s.FinalNoticeDays <= s.SecondReminderDays)))
        {
            return Result.Failure(Error.Validation("Ar.DunningGroup", "A dunning group needs a code, a name and rising reminder days (first, second, final)."));
        }

        DunningGroup? saved = await _db.Set<DunningGroup>().FirstOrDefaultAsync(g => g.CompanyId == companyId && g.Code == code, ct).ConfigureAwait(false);
        if (saved is null)
        {
            saved = new DunningGroup(companyId, code, group.Name);
            _db.Set<DunningGroup>().Add(saved);
        }

        saved.Name = group.Name.Trim();
        (saved.FirstReminderDays, saved.SecondReminderDays, saved.FinalNoticeDays, saved.IsExempt) = (s.FirstReminderDays, s.SecondReminderDays, s.FinalNoticeDays, group.IsExempt);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SetCustomerAsync(long companyId, string customerCode, string? groupCode, string? customerType, CancellationToken ct = default)
    {
        string code = (customerCode ?? string.Empty).Trim().ToUpperInvariant();
        Customer? customer = await _db.Customers.FirstOrDefaultAsync(c => c.CompanyId == companyId && c.Code == code, ct).ConfigureAwait(false);
        if (customer is null)
        {
            return Result.Failure(Error.NotFound("Ar.CustomerNotFound", $"Customer {code} not found."));
        }

        string? group = string.IsNullOrWhiteSpace(groupCode) ? null : groupCode.Trim().ToUpperInvariant();
        if (group is not null && !await _db.Set<DunningGroup>().AnyAsync(g => g.CompanyId == companyId && g.Code == group, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.NotFound("Ar.DunningGroupNotFound", $"Dunning group {group} not found."));
        }

        customer.DunningGroupCode = group;
        customer.CustomerType = string.IsNullOrWhiteSpace(customerType) ? null : customerType.Trim();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<DunningSend>> SendAsync(long companyId, DateOnly asAtDate, DunningStages stages, string templateCode, string language, int resendDays = 14, CancellationToken ct = default)
    {
        if (_messages is null)
        {
            return Error.Rule("Ar.NoMessaging", "Sending reminders is not available.");
        }

        Result<IReadOnlyList<DunningNotice>> assessed = await AssessAsync(companyId, asAtDate, stages, ct).ConfigureAwait(false);
        if (assessed.IsFailure)
        {
            return assessed.Error;
        }

        DateOnly since = asAtDate.AddDays(-Math.Max(0, resendDays));
        List<long> ids = [.. assessed.Value.Select(n => n.CustomerId)];
        var logs = await _db.Set<DunningLog>().AsNoTracking().Where(l => l.CompanyId == companyId && ids.Contains(l.CustomerId) && l.SentOn > since).Select(l => new { l.CustomerId, l.Level })
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, Customer> customers = await _db.Customers.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct).ConfigureAwait(false);

        // Each level can have its own wording (REMINDER1, REMINDER2, REMINDER3 beside REMINDER), and each channel its own template; a customer with no email
        // address is sent an SMS when there is a mobile number and an SMS template, otherwise a letter is printed and posted.
        string baseCode = (templateCode ?? string.Empty).Trim().ToUpperInvariant();
        HashSet<(string Code, Core.Communications.MessageChannel Channel)> templates = [.. (await _db.Set<Core.Communications.MessageTemplate>().AsNoTracking()
            .Where(t => t.CompanyId == companyId && t.Code.StartsWith(baseCode)).Select(t => new { t.Code, t.Channel }).ToListAsync(ct).ConfigureAwait(false))
            .Select(t => (t.Code.ToUpperInvariant(), t.Channel))];
        string? TemplateFor(int level, Core.Communications.MessageChannel channel)
        {
            string forLevel = baseCode + level.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return templates.Contains((forLevel, channel)) ? forLevel : templates.Contains((baseCode, channel)) || channel == Core.Communications.MessageChannel.Email ? baseCode : null;
        }

        Dictionary<(string Code, Core.Communications.MessageChannel Channel), List<Application.Abstractions.Communications.DespatchRecipient>> batches = [];
        List<Application.Abstractions.Communications.DespatchRecipient> recipients = [];
        List<DunningNotice> sent = [];
        List<string> toPost = [];
        int noEmail = 0, already = 0, bySms = 0;
        foreach (DunningNotice notice in assessed.Value)
        {
            if (logs.Any(l => l.CustomerId == notice.CustomerId && l.Level == notice.Level))
            {
                already++;
                continue;
            }

            Customer customer = customers[notice.CustomerId];
            Core.Communications.MessageChannel? picked = !string.IsNullOrWhiteSpace(customer.Email) ? Core.Communications.MessageChannel.Email
                : !string.IsNullOrWhiteSpace(customer.MobileNumber) && TemplateFor(notice.Level, Core.Communications.MessageChannel.Sms) is not null ? Core.Communications.MessageChannel.Sms
                : null;
            if (picked is not Core.Communications.MessageChannel channel)
            {
                noEmail++;
                toPost.Add(customer.Code);
                continue;
            }

            Result<string> letter = await RenderReminderTextAsync(companyId, customer.Id, asAtDate, stages, ct: ct).ConfigureAwait(false);
            if (letter.IsFailure)
            {
                return letter.Error;
            }

            Application.Abstractions.Communications.DespatchRecipient recipient = new(channel == Core.Communications.MessageChannel.Email ? customer.Email!.Trim() : customer.MobileNumber!.Trim(),
                new Dictionary<string, string>
                {
                    ["Name"] = customer.Name, ["Account"] = customer.Code, ["Level"] = notice.LevelLabel,
                    ["Overdue"] = notice.OverdueAmount.ToString("N2", System.Globalization.CultureInfo.InvariantCulture), ["Letter"] = letter.Value,
                });
            (string Code, Core.Communications.MessageChannel Channel) key = (TemplateFor(notice.Level, channel)!, channel);
            if (!batches.TryGetValue(key, out List<Application.Abstractions.Communications.DespatchRecipient>? group))
            {
                batches[key] = group = [];
            }

            group.Add(recipient);
            recipients.Add(recipient);
            bySms += channel == Core.Communications.MessageChannel.Sms ? 1 : 0;
            sent.Add(notice);
        }

        if (recipients.Count == 0)
        {
            return new DunningSend(null, 0, noEmail, already, 0, toPost);
        }

        long? firstBatch = null;
        foreach (((string code, Core.Communications.MessageChannel channel), List<Application.Abstractions.Communications.DespatchRecipient> group) in batches.OrderBy(b => b.Key.Channel).ThenBy(b => b.Key.Code, StringComparer.Ordinal))
        {
            Result<long> batch = await _messages.QueueAsync(companyId, $"Reminders {asAtDate:yyyy-MM-dd} {code} {channel}", code, language, channel, group, ct).ConfigureAwait(false);
            if (batch.IsFailure)
            {
                return batch.Error;
            }

            firstBatch ??= batch.Value;
        }

        _db.Set<DunningLog>().AddRange(sent.Select(n => new DunningLog(companyId, n.CustomerId, n.Level, asAtDate, n.OverdueAmount)));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new DunningSend(firstBatch, recipients.Count, noEmail, already, bySms, toPost);
    }

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

        // A customer in a dunning group is chased on the group's timetable, or not at all when the group is exempt.
        Dictionary<string, DunningGroup> groups = await _db.Set<DunningGroup>().AsNoTracking().Where(g => g.CompanyId == companyId).ToDictionaryAsync(g => g.Code, ct).ConfigureAwait(false);
        Dictionary<long, string> membership = groups.Count == 0 ? [] : await _db.Customers.AsNoTracking().Where(c => c.CompanyId == companyId && c.DunningGroupCode != null)
            .ToDictionaryAsync(c => c.Id, c => c.DunningGroupCode!, ct).ConfigureAwait(false);
        DunningGroup? GroupOf(long customerId) => membership.TryGetValue(customerId, out string? code) ? groups.GetValueOrDefault(code) : null;

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
            DunningGroup? group = GroupOf(byCustomer.Key);
            if (group is { IsExempt: true })
            {
                continue;
            }

            NetOverdue net = Dunning.ApplyCredits(byCustomer.Select(o => new OverdueItem(o.DueDate, o.OutstandingBase)), credits.GetValueOrDefault(byCustomer.Key), asAtDate);
            int level = Dunning.LevelFor(net.OldestDaysOverdue, group is null ? stages : new DunningStages(group.FirstReminderDays, group.SecondReminderDays, group.FinalNoticeDays));
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
            CustomerAddress = await TraderDirectory.CorrespondenceAsync(_db, companyId, TradingLedger.AccountsReceivable, customerId, ct).ConfigureAwait(false),
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
