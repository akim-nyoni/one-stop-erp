using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Abstractions.Municipal;
using One4All.Core.Communications;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers billing runs limited to a meter-reading route or to a billing cycle, and emailing a posted run's bills: only the route's stands are
/// billed, a cycle run picks up only the stands on that cycle's tariffs, and each ratepayer billed with an email address is sent their bill.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class RouteBillingRunTests
{
    private readonly AccountingFixture _fixture;

    public RouteBillingRunTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Runs_follow_a_route_or_a_cycle_and_their_bills_are_emailed()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Tariff monthly = new(companyId, "RR-REF", "Refuse", ServiceType.Refuse, TariffType.Flat, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 20m };
        Tariff quarterly = new(companyId, "RR-REFQ", "Refuse (quarterly)", ServiceType.Refuse, TariffType.Flat, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 60m };
        quarterly.SetCycle(BillingCycle.Quarterly, 1, 1);
        Customer alpha = new(companyId, "RR-A", "Alpha", Scenario.Usd) { Email = "alpha@example.com" }, beta = new(companyId, "RR-B", "Beta", Scenario.Usd),
            gamma = new(companyId, "RR-C", "Gamma", Scenario.Usd) { Email = "gamma@example.com" };
        Meter meterA = new(companyId, "RR-M1"), meterB = new(companyId, "RR-M2"), meterC = new(companyId, "RR-M3");
        db.AddRange(monthly, quarterly, alpha, beta, gamma, meterA, meterB, meterC);
        await db.SaveChangesAsync();
        Property Stand(string number, Customer owner, Tariff tariff, Meter meter)
        {
            Property stand = new(companyId, number, "Ward 4", owner.Id);
            stand.AddService(ServiceType.Refuse, tariff.Id, new DateOnly(2026, 1, 1)).MeterId = meter.Id;
            db.Properties.Add(stand);
            return stand;
        }

        Stand("RR-1", alpha, monthly, meterA);
        Stand("RR-2", beta, monthly, meterB);
        Stand("RR-3", gamma, quarterly, meterC);
        ReadingRoute route = new(companyId, "RR1", "North walk", "Reader");
        route.AddStop(meterA.Id);
        route.AddStop(meterB.Id);
        db.Add(route);
        await db.SaveChangesAsync();

        IBillingRunService runs = scope.ServiceProvider.GetRequiredService<IBillingRunService>();
        BillingRunProposal Proposal(string? routeCode, BillingCycle? cycle) => new()
        {
            CompanyId = companyId, ServiceType = ServiceType.Refuse, BillDate = new DateOnly(2026, 5, 31), Ward = "Ward 4", InvoicePrefix = "RR", UserId = 81, RouteCode = routeCode, Cycle = cycle,
        };
        (await runs.ProposeAsync(Proposal("NOWHERE", null))).Error.Code.Should().Be("Mun.RouteNotFound");
        BillingRunSummary byRoute = (await runs.ProposeAsync(Proposal("rr1", null))).Value;
        byRoute.Included.Should().Be(2, "only the stands with a meter on the route");
        (await runs.DespatchBillsAsync(companyId, byRoute.RunNumber, "BILL", "en")).Error.Code.Should().Be("Mun.RunNotPosted");
        (await runs.ApproveAsync(companyId, byRoute.RunNumber, 82)).Value.Status.Should().Be(BillingRunStatus.Posted);

        (await scope.ServiceProvider.GetRequiredService<INotificationService>().SaveTemplateAsync(companyId, "BILL", "Bill", "en", MessageChannel.Email, "Your bill {{Account}}",
            "Dear {{Name}},\nYou owe {{Currency}} {{Balance}}.\n\n{{Statement}}")).IsSuccess.Should().BeTrue();
        StatementDespatch sent = (await runs.DespatchBillsAsync(companyId, byRoute.RunNumber, "BILL", "en")).Value;
        (sent.Queued, sent.NoEmail).Should().Be((1, 1), "Alpha has an email address, Beta's bill is printed");

        BillingRunSummary byCycle = (await runs.ProposeAsync(Proposal(null, BillingCycle.Quarterly))).Value;
        (byCycle.Included, byCycle.Total).Should().Be((1, 60m), "only Gamma's stand is on the quarterly tariff");
    }
}
