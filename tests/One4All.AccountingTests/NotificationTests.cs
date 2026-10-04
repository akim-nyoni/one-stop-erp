using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Communications;
using One4All.Core.Communications;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers notifications and communications: templates per language with fallback to English and a preview; a bulk email
/// despatch where an unmergeable recipient fails at once, a bouncing address is retried and then fails, and the rest are
/// sent; a channel with no gateway stays queued with the reason; in-app messages land in the user's notification centre,
/// which can be read and marked read.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class NotificationTests
{
    private readonly AccountingFixture _fixture;

    public NotificationTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Templates_are_merged_despatched_with_retry_and_delivered_in_app()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        FakeEmailGateway email = new();
        await using ServiceProvider provider = _fixture.BuildServiceProvider(services => services.AddSingleton<IMessageGateway>(email));

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        User clerk = new($"msg-clerk-{companyId}", "Billing Clerk");
        db.Users.Add(clerk);
        await db.SaveChangesAsync();

        INotificationService messages = scope.ServiceProvider.GetRequiredService<INotificationService>();
        (await messages.SaveTemplateAsync(companyId, "stmt", "Statement", "en", MessageChannel.Email, "", "Body")).Error.Code.Should().Be("Msg.Template");
        (await messages.SaveTemplateAsync(companyId, "stmt", "Statement", "EN", MessageChannel.Email, "Statement {{Account}}", "Dear {{Name}}, your balance is {{Balance}}."))
            .Value.Should().Equal("Account", "Name", "Balance");
        (await messages.SaveTemplateAsync(companyId, "STMT", "Statement", "sn", MessageChannel.Email, "Chitatimendi {{Account}}", "Mhoro {{Name}}, mari yamunoda ndeye {{Balance}}.")).IsSuccess.Should().BeTrue();

        Dictionary<string, string> sample = new() { ["Account"] = "C-1", ["Name"] = "T. Dube", ["Balance"] = "320.00" };
        (await messages.PreviewAsync(companyId, "stmt", "sn", MessageChannel.Email, sample)).Value.Subject.Should().Be("Chitatimendi C-1");
        (await messages.PreviewAsync(companyId, "stmt", "nd", MessageChannel.Email, sample)).Value.Body.Should().Be("Dear T. Dube, your balance is 320.00."); // falls back to English
        (await messages.PreviewAsync(companyId, "stmt", "en", MessageChannel.Sms, sample)).Error.Code.Should().Be("Msg.TemplateNotFound");

        // Bulk statements: one recipient cannot be merged, one address bounces.
        long batch = (await messages.QueueAsync(companyId, "March statements", "STMT", "en", MessageChannel.Email,
        [
            new("a@example.com", sample),
            new("bounce@example.com", new Dictionary<string, string>(sample) { ["Account"] = "C-2" }),
            new("c@example.com", new Dictionary<string, string> { ["Account"] = "C-3", ["Name"] = "No Balance" }),
        ])).Value;
        (await messages.BatchStatusAsync(companyId, batch)).Value.Should().BeEquivalentTo(new { Queued = 2, Sent = 0, Failed = 1 });

        (await messages.DespatchAsync(companyId, 2)).Should().Be(new DespatchRun(1, 1, 0));
        (await messages.DespatchAsync(companyId, 2)).Should().Be(new DespatchRun(0, 0, 1));
        BatchStatus status = (await messages.BatchStatusAsync(companyId, batch)).Value;
        (status.Queued, status.Sent, status.Failed).Should().Be((0, 1, 2));
        status.Failures.Should().Equal("bounce@example.com: Mailbox unavailable", "c@example.com: Missing merge fields: Balance.");
        email.Sent.Should().Equal(("a@example.com", "Statement C-1", "Dear T. Dube, your balance is 320.00."));

        // No SMS gateway: the message waits with the reason; in-app goes straight to the user's centre.
        (await messages.SaveTemplateAsync(companyId, "REMIND", "Fee reminder", "en", MessageChannel.Sms, string.Empty, "Fees of {{Amount}} are due.")).IsSuccess.Should().BeTrue();
        (await messages.SaveTemplateAsync(companyId, "REMIND", "Fee reminder", "en", MessageChannel.InApp, "Fees due", "Fees of {{Amount}} are due.")).IsSuccess.Should().BeTrue();
        long sms = (await messages.QueueAsync(companyId, "SMS reminders", "REMIND", "en", MessageChannel.Sms, [new("+263770000000", new Dictionary<string, string> { ["Amount"] = "50" })])).Value;
        long inApp = (await messages.QueueAsync(companyId, "In-app reminders", "REMIND", "en", MessageChannel.InApp, [new(clerk.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), new Dictionary<string, string> { ["Amount"] = "50" })])).Value;
        (await messages.DespatchAsync(companyId, 3)).Should().Be(new DespatchRun(1, 1, 0));
        (await messages.BatchStatusAsync(companyId, sms)).Value.Queued.Should().Be(1);
        (await messages.BatchStatusAsync(companyId, inApp)).Value.Sent.Should().Be(1);

        (await messages.NotifyUserAsync(companyId, clerk.Id, "Approval waiting", "PO-12 needs your approval.")).IsSuccess.Should().BeTrue();
        IReadOnlyList<NotificationEntry> inbox = await messages.InboxAsync(companyId, clerk.Id, unreadOnly: true);
        inbox.Select(n => n.Title).Should().Equal("Approval waiting", "Fees due");
        (await messages.MarkReadAsync(companyId, clerk.Id, inbox[1].Id)).IsSuccess.Should().BeTrue();
        (await messages.MarkReadAsync(companyId, clerk.Id + 999_999, inbox[0].Id)).Error.Code.Should().Be("Msg.NotificationNotFound");
        (await messages.InboxAsync(companyId, clerk.Id, unreadOnly: true)).Select(n => n.Title).Should().Equal("Approval waiting");
        (await messages.InboxAsync(companyId, clerk.Id, unreadOnly: false)).Should().HaveCount(2);
    }

    private sealed class FakeEmailGateway : IMessageGateway
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = [];

        public MessageChannel Channel => MessageChannel.Email;

        public Task<GatewayResult> SendAsync(long companyId, string recipient, string subject, string body, CancellationToken ct = default)
        {
            if (recipient.StartsWith("bounce", StringComparison.Ordinal))
            {
                return Task.FromResult(new GatewayResult(false, "Mailbox unavailable"));
            }

            Sent.Add((recipient, subject, body));
            return Task.FromResult(new GatewayResult(true, $"MSG-{Sent.Count}"));
        }
    }
}
