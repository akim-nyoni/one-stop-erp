using Microsoft.EntityFrameworkCore;
using One4All.Core.Communications;

namespace One4All.Infrastructure.Persistence.Seeding;

/// <summary>
/// Seeds the standard message templates every company starts with (brief §8): the customer statement (STATEMENT), the school
/// fee statement (FEES), the dunning reminder (REMINDER) and the municipal bill (BILL, by email and by SMS), in English. A
/// template a company already has - reworded or not - is left alone; only the missing ones are added, so the seeder is safe to
/// run at every start-up and after a new company is created.
/// </summary>
public sealed class MessageTemplateSeeder
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the seeder.</summary>
    /// <param name="db">The database context.</param>
    public MessageTemplateSeeder(One4AllDbContext db) => _db = db;

    /// <summary>The language the standard templates are seeded in.</summary>
    public const string Language = "en";

    /// <summary>The standard templates: code, document type, channel, subject and body with their merge fields.</summary>
    public static readonly IReadOnlyList<(string Code, string DocumentType, MessageChannel Channel, string Subject, string Body)> Standard =
    [
        ("STATEMENT", "Statement", MessageChannel.Email, "Statement of account {{Account}} as at {{AsAt}}",
            "Dear {{Name}},\n\nPlease find your statement of account {{Account}} as at {{AsAt}}. The balance outstanding is {{Currency}} {{Balance}}.\n\n{{Statement}}\n\nKindly settle any overdue amount, or let us know of any query.\n\nThank you for your business."),
        ("FEES", "FeeStatement", MessageChannel.Email, "School fees: statement for {{Name}} ({{Account}})",
            "Dear Parent or Guardian,\n\nThis is the fee statement for {{Name}}, account {{Account}}, as at {{AsAt}}. The balance outstanding is {{Currency}} {{Balance}}.\n\n{{Statement}}\n\nPlease pay the balance by the due date to keep the learner's place. Thank you."),
        ("REMINDER", "Dunning", MessageChannel.Email, "Payment reminder ({{Level}}): account {{Account}}",
            "Dear {{Name}},\n\nOur records show {{Overdue}} overdue on account {{Account}}.\n\n{{Letter}}\n\nIf you have already paid, please disregard this reminder."),
        ("BILL", "Bill", MessageChannel.Email, "Your bill for {{BillDate}}: account {{Account}}",
            "Dear {{Name}},\n\nYour bill for {{BillDate}} on account {{Account}} ({{Stands}}) is attached. This bill: {{Currency}} {{Amount}}. Balance now due: {{Currency}} {{Balance}}.\n\n{{Bills}}\n\nPlease pay by the due date to avoid interest and disconnection."),
        ("BILL", "Bill", MessageChannel.Sms, string.Empty,
            "{{Name}}, your {{BillDate}} bill on account {{Account}} is {{Currency}} {{Amount}}; balance due {{Currency}} {{Balance}}. Please pay by the due date."),
    ];

    /// <summary>Adds the standard templates every company is missing.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>How many templates were added.</returns>
    public async Task<int> SeedAsync(CancellationToken ct = default)
    {
        List<long> companies = await _db.Companies.AsNoTracking().Select(c => c.Id).ToListAsync(ct).ConfigureAwait(false);
        int added = 0;
        foreach (long companyId in companies)
        {
            added += await SeedAsync(companyId, ct).ConfigureAwait(false);
        }

        return added;
    }

    /// <summary>Adds the standard templates one company is missing.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>How many templates were added.</returns>
    public async Task<int> SeedAsync(long companyId, CancellationToken ct = default)
    {
        HashSet<(string Code, MessageChannel Channel)> present = [.. (await _db.Set<MessageTemplate>().AsNoTracking()
            .Where(t => t.CompanyId == companyId && t.Language == Language).Select(t => new { t.Code, t.Channel }).ToListAsync(ct).ConfigureAwait(false)).Select(t => (t.Code, t.Channel))];
        int added = 0;
        foreach ((string code, string documentType, MessageChannel channel, string subject, string body) in Standard)
        {
            if (present.Contains((code, channel)))
            {
                continue;
            }

            _db.Set<MessageTemplate>().Add(new MessageTemplate(companyId, code, documentType, Language, channel, subject, body));
            added++;
        }

        if (added > 0)
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return added;
    }
}
