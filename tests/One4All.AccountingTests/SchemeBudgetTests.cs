using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers scheme budgets (brief §14 H84): professional fees capitalised over three unsold stands (cents on the last), one
/// stand sold, then overheads spread over the two left; the stands account carries it all; and budget vs actual shows land,
/// fees and overheads against their budgets.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SchemeBudgetTests
{
    private readonly AccountingFixture _fixture;

    public SchemeBudgetTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Scheme_costs_are_capitalised_to_unsold_stands_and_compared_with_budget()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        Customer buyer = new(companyId, "SB-BUY", "Budget Buyer", Scenario.Usd);
        db.Customers.Add(buyer);
        HousingScheme scheme = new(companyId, $"SCH-SB-{companyId}", "Budget Scheme", SchemeType.CouncilDelivered);
        db.HousingSchemes.Add(scheme);
        await db.SaveChangesAsync();

        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
        long parcelId = (await housing.AcquireLandAsync(companyId, $"LP-SB-{companyId}", "Parcel", 900m, 9000m, new DateOnly(2026, 1, 2), "1450", "1100", "LAND-SB")).Value.LandParcelId;
        IReadOnlyList<long> stands = (await housing.SubdivideAsync(companyId, parcelId, scheme.Id, [new NewStand("SB-1", 300m), new NewStand("SB-2", 300m), new NewStand("SB-3", 300m)],
            ApportionMethod.Equal, new DateOnly(2026, 1, 5), "1450", "1460")).Value.StandIds;

        ISchemeBudgetService budgets = scope.ServiceProvider.GetRequiredService<ISchemeBudgetService>();
        (await budgets.SetBudgetAsync(companyId, scheme.Code, SchemeCostElement.Land, 10000m)).IsSuccess.Should().BeTrue();
        (await budgets.SetBudgetAsync(companyId, scheme.Code, SchemeCostElement.ProfessionalFees, 800m)).IsSuccess.Should().BeTrue();
        (await budgets.SetBudgetAsync(companyId, scheme.Code, SchemeCostElement.ProfessionalFees, 1000m)).IsSuccess.Should().BeTrue();
        (await budgets.SetBudgetAsync(companyId, scheme.Code, SchemeCostElement.Land, -1m)).Error.Code.Should().Be("Hse.BudgetDetails");

        SchemeCostRequest Cost(SchemeCostElement element, decimal amount, string reference) => new()
        {
            CompanyId = companyId, SchemeCode = scheme.Code, Element = element, Date = new DateOnly(2026, 2, 1), Amount = amount, Reference = reference, Description = "Engineer",
            StandsAccountCode = "1460", FundingAccountCode = "1100",
        };
        (await budgets.RecordCostAsync(Cost(SchemeCostElement.Servicing, 100m, "X-1"))).Error.Code.Should().Be("Hse.SchemeCostDetails");
        (await budgets.RecordCostAsync(Cost(SchemeCostElement.ProfessionalFees, 1000.01m, "PF-1"))).IsSuccess.Should().BeTrue();
        (await housing.SellStandAsync(companyId, stands[0], buyer.Id, buyer.Name, 6000m, new DateOnly(2026, 2, 10), "4100", "1460", "5100", "INV-SB-1")).IsSuccess.Should().BeTrue();
        (await budgets.RecordCostAsync(Cost(SchemeCostElement.Overheads, 300m, "OH-1") with { Date = new DateOnly(2026, 2, 20) })).IsSuccess.Should().BeTrue();

        Dictionary<string, decimal> costs = db.HousingStands.Where(s => s.SchemeId == scheme.Id).ToDictionary(s => s.StandNumber, s => s.AllocatedCost);
        costs.Should().BeEquivalentTo(new Dictionary<string, decimal> { ["SB-1"] = 3333.33m, ["SB-2"] = 3483.33m, ["SB-3"] = 3483.35m });
        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 12, 31))).Value;
        tb.Rows.Where(r => r.AccountCode == "1460").Sum(r => r.Debit - r.Credit).Should().Be(6966.68m); // the two unsold stands

        var rendered = await scope.ServiceProvider.GetRequiredService<IReportRunner>().RenderAsync(companyId, "H84", new Dictionary<string, object?>(), ReportFormat.Csv);
        rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? rendered.Error.Message : null);
        string report = Encoding.UTF8.GetString(rendered.Value.Content);
        report.Should().Contain($"{scheme.Code},Land,\"10,000.00\",\"9,000.00\",90.0,1000").And.Contain($"{scheme.Code},Servicing,-,0.00,-,0")
            .And.Contain($"{scheme.Code},ProfessionalFees,\"1,000.00\",\"1,000.01\",100.0,-0.01").And.Contain($"{scheme.Code},Overheads,-,300.00,-,-300");
    }
}
