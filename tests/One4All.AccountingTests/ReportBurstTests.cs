using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Communications;
using One4All.Core.Reporting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers bursting: the aged debtors report split by customer, each part a PDF of that customer's rows only, emailed to the customer's
/// statement contact (or the customer's own email) and the customer with no address listed; and a subscription that bursts on every run,
/// logging each part's delivery.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ReportBurstTests
{
    private readonly AccountingFixture _fixture;

    public ReportBurstTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_report_is_burst_to_each_customer()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        string tag = Guid.NewGuid().ToString("N")[..6];
        Customer contactCustomer = new(companyId, "RB-A", $"Alpha Traders {tag}", Scenario.Usd);
        Customer emailCustomer = new(companyId, "RB-B", $"Beta Stores {tag}", Scenario.Usd) { Email = "accounts@beta.example" };
        Customer silent = new(companyId, "RB-C", $"Gamma Ltd {tag}", Scenario.Usd);
        db.AddRange(contactCustomer, emailCustomer, silent);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<ITraderDirectoryService>().AddContactAsync(companyId, TradingLedger.AccountsReceivable, "RB-A",
            new NewTraderContact("Anesu", "Accounts", "anesu@alpha.example", ReceivesStatements: true))).IsSuccess.Should().BeTrue();
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        foreach ((Customer customer, string number, decimal amount) in new[] { (contactCustomer, "RB-1", 100m), (contactCustomer, "RB-2", 50m), (emailCustomer, "RB-3", 70m), (silent, "RB-4", 30m) })
        {
            (await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = number, DocumentDate = new DateOnly(2026, 4, 1), Currency = Scenario.Usd, NetAmount = amount, SalesAccountCode = "4000",
            })).IsSuccess.Should().BeTrue();
        }

        IReportBurstService burst = scope.ServiceProvider.GetRequiredService<IReportBurstService>();
        Dictionary<string, object?> parameters = new() { ["AsAtDate"] = new DateOnly(2026, 6, 30) };
        (await burst.BurstAsync(new BurstRequest(companyId, "AR29", "Customer") { Parameters = parameters })).Error.Code.Should().Be("Rpt.BurstRecipients");
        (await burst.BurstAsync(new BurstRequest(companyId, "AR29", "NoSuchField") { Parameters = parameters, Ledger = TradingLedger.AccountsReceivable })).Error.Code.Should().Be("Rpt.BurstKey");

        IReadOnlyList<BurstDelivery> sent = (await burst.BurstAsync(new BurstRequest(companyId, "AR29", "Customer") { Parameters = parameters, Ledger = TradingLedger.AccountsReceivable })).Value;
        sent.Select(d => d.Key).Should().Equal(contactCustomer.Name, emailCustomer.Name, silent.Name);
        sent[0].Recipients.Should().Equal("anesu@alpha.example");
        sent[1].Recipients.Should().Equal("accounts@beta.example");
        sent[2].Recipients.Should().BeEmpty();
        OutboundMessage alpha = await db.Set<OutboundMessage>().AsNoTracking().SingleAsync(m => m.BatchId == sent[0].BatchId);
        (alpha.AttachmentContentType, alpha.AttachmentName).Should().Be(("application/pdf", sent[0].FileName));
        System.Text.Encoding.ASCII.GetString(alpha.AttachmentContent!, 0, 5).Should().Be("%PDF-");
        sent[0].FileName.Should().StartWith("ar29-alpha-traders");

        // Each part holds its own customer's rows only.
        IReadOnlyList<BurstSlice> slices = (await scope.ServiceProvider.GetRequiredService<IReportRunner>().BurstAsync(companyId, "AR29", parameters, ReportFormat.Csv, "Customer",
            One4All.Application.Reporting.ReportAccess.Unrestricted)).Value;
        string alphaCsv = System.Text.Encoding.UTF8.GetString(slices.Single(x => x.Key == contactCustomer.Name).Document.Content);
        alphaCsv.Should().Contain("RB-1").And.Contain("RB-2").And.NotContain("RB-3").And.NotContain("RB-4");

        // A subscription bursting on every run.
        IReportSubscriptionService subscriptions = scope.ServiceProvider.GetRequiredService<IReportSubscriptionService>();
        string code = $"RB-SUB-{tag}";
        (await subscriptions.SubscribeAsync(new SubscriptionRequest { CompanyId = companyId, Code = code, ReportCode = "AR29", BurstKeyField = "Customer" })).Error.Code.Should().Be("Rpt.BurstRecipients");
        (await subscriptions.SubscribeAsync(new SubscriptionRequest
        {
            CompanyId = companyId, Code = code, ReportCode = "AR29", BurstKeyField = "Customer", BurstLedger = TradingLedger.AccountsReceivable,
            Parameters = new Dictionary<string, string> { ["AsAtDate"] = "2026-06-30" },
        })).IsSuccess.Should().BeTrue();
        (await subscriptions.RunAsync(companyId, code, DateTimeOffset.UtcNow)).Value.Should().Be(2);
        IReadOnlyList<DeliveryLogEntry> log = (await subscriptions.DeliveryLogAsync(companyId, code)).Value;
        log.Where(l => l.Succeeded).Select(l => l.Recipient).Should().BeEquivalentTo(["anesu@alpha.example", "accounts@beta.example"]);
        log.Should().ContainSingle(l => !l.Succeeded && l.Detail.Contains(silent.Name, StringComparison.Ordinal));
    }
}
