using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Integration;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Integration;
using One4All.Core.Integration;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers webhooks end to end: posting an invoice queues a signed delivery for its subscriber in the same unit of work; a refused
/// delivery is retried after the back-off and then delivered; a stopped subscription receives nothing more.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class WebhookTests
{
    private readonly AccountingFixture _fixture;

    public WebhookTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Invoices_are_published_signed_and_retried()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        List<(string? Event, string? Signature, string Body)> received = [];
        HttpStatusCode answer = HttpStatusCode.InternalServerError;
        StubHandler handler = new(async request =>
        {
            received.Add((request.Headers.GetValues("X-One4All-Event").Single(), request.Headers.GetValues("X-One4All-Signature").Single(), await request.Content!.ReadAsStringAsync()));
            return new HttpResponseMessage(answer);
        });
        await using ServiceProvider provider = _fixture.BuildServiceProvider(s => s.AddSingleton(new HttpClient(handler)));
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer customer = new(companyId, "WH-CUST", "Hooked", Scenario.Usd);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        IWebhookService hooks = scope.ServiceProvider.GetRequiredService<IWebhookService>();
        (await hooks.SubscribeAsync(companyId, "order.shipped", "https://hooks.example.test/in", "0123456789abcdef")).Error.Code.Should().Be("Webhook.Invalid");
        (await hooks.SubscribeAsync(companyId, WebhookEvents.InvoicePosted, "ftp://hooks.example.test", "0123456789abcdef")).Error.Code.Should().Be("Webhook.Invalid");
        long subscription = (await hooks.SubscribeAsync(companyId, WebhookEvents.InvoicePosted, "https://hooks.example.test/in", "0123456789abcdef")).Value;

        ISalesLedgerService sales = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        (await sales.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "WH-INV1", DocumentDate = new DateOnly(2026, 4, 1), Currency = Scenario.Usd, NetAmount = 120m, SalesAccountCode = "4000",
        })).IsSuccess.Should().BeTrue();
        WebhookDelivery queued = await db.Set<WebhookDelivery>().AsNoTracking().SingleAsync(d => d.SubscriptionId == subscription);
        queued.Payload.Should().Contain("\"event\": \"invoice.posted\"").And.Contain("WH-INV1");

        // Refused, then retried after the back-off and accepted.
        DateTimeOffset now = DateTimeOffset.UtcNow.AddSeconds(5);
        (await hooks.DispatchDueAsync(now)).Should().Be(new WebhookDispatchResult(0, 1, 0));
        (await hooks.DispatchDueAsync(now.AddSeconds(30))).Should().Be(new WebhookDispatchResult(0, 0, 0));
        answer = HttpStatusCode.OK;
        (await hooks.DispatchDueAsync(now.AddMinutes(2))).Should().Be(new WebhookDispatchResult(1, 0, 0));
        received.Should().HaveCount(2).And.OnlyContain(r => r.Event == WebhookEvents.InvoicePosted && r.Signature == WebhookRules.Signature("0123456789abcdef", r.Body));
        (await db.Set<WebhookDelivery>().AsNoTracking().SingleAsync(d => d.Id == queued.Id)).Should().Match<WebhookDelivery>(d => d.Status == WebhookDeliveryStatus.Delivered && d.Attempts == 2);

        // A stopped subscription receives nothing more.
        (await hooks.UnsubscribeAsync(companyId, subscription)).IsSuccess.Should().BeTrue();
        (await hooks.PublishAsync(companyId, WebhookEvents.InvoicePosted, new { Test = true })).Should().Be(0);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
