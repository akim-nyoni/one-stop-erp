using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Communications;
using One4All.Core.Communications;
using One4All.Core.Trading;
using One4All.Infrastructure.Communications;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers WhatsApp: the Cloud API request (text, or a template carrying the text) with the bearer token; a queued message despatched
/// through it keeps the provider id; signed webhook deliveries apply delivered / read / failed receipts (in any order) and record a
/// customer's reply once, matched by number; unsigned or wrongly signed deliveries and a wrong verify token are refused.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class WhatsAppTests
{
    private const string Secret = "app-secret-for-tests";
    private readonly AccountingFixture _fixture;

    public WhatsAppTests(AccountingFixture fixture) => _fixture = fixture;

    [Fact]
    public void The_request_is_text_or_a_template()
    {
        WhatsAppCloudGateway.RequestBody("263779876543", "Hello", new WhatsAppOptions()).Should().Contain("\"type\":\"text\"").And.Contain("\"body\":\"Hello\"");
        string template = WhatsAppCloudGateway.RequestBody("263779876543", "Your statement", new WhatsAppOptions { TemplateName = "statement_ready", TemplateLanguage = "en_GB" });
        template.Should().Contain("\"name\":\"statement_ready\"").And.Contain("\"code\":\"en_GB\"").And.Contain("\"text\":\"Your statement\"");
    }

    [SkippableFact]
    public async Task Messages_go_out_and_receipts_and_replies_come_back()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        string wamid = $"wamid.{Guid.NewGuid():N}";
        HttpRequestMessage? seen = null;
        string? sent = null;
        HttpClient http = new(new StubHandler(async request =>
        {
            seen = request;
            sent = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{{\"messages\":[{{\"id\":\"{wamid}\"}}]}}") };
        }));
        MessagingOptions options = new() { WhatsApp = new WhatsAppOptions { PhoneNumberId = "1055", AccessToken = "tok", AppSecret = Secret, VerifyToken = "verify-me" } };
        await using ServiceProvider provider = _fixture.BuildServiceProvider(s =>
        {
            s.AddSingleton(options);
            s.AddSingleton<IMessageGateway>(new WhatsAppCloudGateway(http, options));
        });
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        options.WhatsApp.CompanyId = companyId;
        Customer customer = new(companyId, "WA-C", "Chipo Dube", Scenario.Usd) { MobileNumber = "077 987 6543" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        // Out: queued, despatched through the Cloud API, the provider id kept.
        INotificationService notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        long batch = (await notifications.QueueDirectAsync(companyId, "WA test", MessageChannel.WhatsApp, ["+263 77 987 6543"], string.Empty, "Your account is ready.", null)).Value;
        (await notifications.DespatchAsync(companyId, 3)).Sent.Should().BeGreaterThanOrEqualTo(1);
        seen!.RequestUri!.ToString().Should().Be("https://graph.facebook.com/v21.0/1055/messages");
        seen.Headers.Authorization!.ToString().Should().Be("Bearer tok");
        sent.Should().Contain("\"to\":\"263779876543\"").And.Contain("Your account is ready.");
        OutboundMessage message = await db.Set<OutboundMessage>().AsNoTracking().SingleAsync(m => m.BatchId == batch);
        (message.Status, message.ProviderReference).Should().Be((MessageStatus.Sent, wamid));

        // Back: read arrives before delivered; the reply is recorded once and matched to the customer.
        IWhatsAppWebhookService webhook = scope.ServiceProvider.GetRequiredService<IWhatsAppWebhookService>();
        webhook.VerifySubscription("subscribe", "verify-me", "12345").Should().Be("12345");
        webhook.VerifySubscription("subscribe", "wrong", "12345").Should().BeNull();
        string replyId = $"wamid.in.{Guid.NewGuid():N}";
        byte[] delivery = Encoding.UTF8.GetBytes($$$"""
            {"object":"whatsapp_business_account","entry":[{"id":"1","changes":[{"field":"messages","value":{"messaging_product":"whatsapp",
              "contacts":[{"profile":{"name":"Chipo"},"wa_id":"263779876543"}],
              "messages":[{"from":"263779876543","id":"{{{replyId}}}","timestamp":"1791300000","type":"text","text":{"body":"Thanks, can I pay on Friday?"}}],
              "statuses":[{"id":"{{{wamid}}}","status":"read","timestamp":"1791300060"},{"id":"{{{wamid}}}","status":"delivered","timestamp":"1791300030"},
                          {"id":"wamid.unknown","status":"delivered","timestamp":"1791300030"}]}}]}]}
            """);
        (await webhook.ProcessAsync(delivery, null)).Error.Code.Should().Be("WhatsApp.Signature");
        (await webhook.ProcessAsync(delivery, WhatsAppWebhookService.Sign(delivery, "other-secret"))).Error.Code.Should().Be("WhatsApp.Signature");
        WhatsAppWebhookResult first = (await webhook.ProcessAsync(delivery, WhatsAppWebhookService.Sign(delivery, Secret))).Value;
        first.Should().Be(new WhatsAppWebhookResult(1, 1, 2));
        (await webhook.ProcessAsync(delivery, WhatsAppWebhookService.Sign(delivery, Secret))).Value.Should().Be(new WhatsAppWebhookResult(0, 0, 4), "a repeated delivery changes nothing");

        message = await db.Set<OutboundMessage>().AsNoTracking().SingleAsync(m => m.Id == message.Id);
        message.ReadAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1791300060));
        message.DeliveredAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1791300060), "read implies delivered, and the later delivered receipt does not undo it");

        IReadOnlyList<InboundMessageLine> inbox = await webhook.UnhandledAsync(companyId);
        inbox.Should().ContainSingle(m => m.Body == "Thanks, can I pay on Friday?" && m.SenderName == "Chipo" && m.CustomerId == customer.Id);
        (await webhook.MarkHandledAsync(companyId, inbox[0].Id, 7)).IsSuccess.Should().BeTrue();
        (await webhook.UnhandledAsync(companyId)).Should().BeEmpty();
        (await webhook.MarkHandledAsync(companyId, inbox[0].Id, 7)).Error.Code.Should().Be("WhatsApp.NotOpen");

        // A failure receipt for a message never delivered fails it, with the provider's reason.
        OutboundMessage undeliverable = new(companyId, null, MessageChannel.WhatsApp, "263770000000", string.Empty, "x", DateTimeOffset.UtcNow);
        string failedId = $"wamid.f.{Guid.NewGuid():N}";
        undeliverable.MarkSent(failedId, DateTimeOffset.UtcNow);
        db.Add(undeliverable);
        await db.SaveChangesAsync();
        byte[] failure = Encoding.UTF8.GetBytes($$$"""{"entry":[{"changes":[{"value":{"statuses":[{"id":"{{{failedId}}}","status":"failed","timestamp":"1791300100","errors":[{"code":131047,"title":"Re-engagement message"}]}]}}]}]}""");
        (await webhook.ProcessAsync(failure, WhatsAppWebhookService.Sign(failure, Secret))).Value.Receipts.Should().Be(1);
        OutboundMessage failed = await db.Set<OutboundMessage>().AsNoTracking().SingleAsync(m => m.Id == undeliverable.Id);
        (failed.Status, failed.LastError).Should().Be((MessageStatus.Failed, "131047: Re-engagement message"));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
