using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the housing money reports (brief §14 H17, H48, H94, H97): two stands created in a ward's scheme, one sold on
/// instalments; the stand movement reconciles to the stands control account; the deposit shows as a collection; stand
/// sales head the revenue summary; and the ward pack counts the ward's stands and arrears.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class HousingMoneyReportsTests
{
    private readonly AccountingFixture _fixture;

    public HousingMoneyReportsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Movement_collections_revenue_and_ward_reports_reflect_the_ledgers()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        db.Accounts.Add(new LedgerAccount(companyId, "4965", "Housing Interest Income", AccountType.Income));
        Customer buyer = new(companyId, "HM-BUY", "Money Buyer", Scenario.Usd);
        db.Customers.Add(buyer);
        HousingScheme scheme = new(companyId, $"SCH-HM-{companyId}", "Ward Scheme", SchemeType.CouncilDelivered) { Ward = "W7" };
        db.HousingSchemes.Add(scheme);
        await db.SaveChangesAsync();

        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
        long parcelId = (await housing.AcquireLandAsync(companyId, $"LP-HM-{companyId}", "Parcel", 1000m, 10000m, new DateOnly(2026, 1, 2), "1450", "1100", "LAND-HM")).Value.LandParcelId;
        IReadOnlyList<long> stands = (await housing.SubdivideAsync(companyId, parcelId, scheme.Id, [new NewStand("HM-1", 500m), new NewStand("HM-2", 500m)],
            ApportionMethod.Equal, new DateOnly(2026, 1, 5), "1450", "1460")).Value.StandIds;
        (await scope.ServiceProvider.GetRequiredService<IInstalmentSaleService>().SignAsync(new InstalmentSaleRequest
        {
            CompanyId = companyId, StandId = stands[0], BuyerCustomerId = buyer.Id, CashPrice = 13000m, Deposit = 1000m, AnnualRatePercent = 12m, Method = InterestMethod.ReducingBalance,
            TermMonths = 12, SignedOn = new DateOnly(2026, 1, 10), FirstDueDate = new DateOnly(2026, 2, 1), SalesAccountCode = "4100", StandsAvailableAccountCode = "1460",
            CostOfSalesAccountCode = "5100", InterestAccountCode = "4965", BankAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();
        (await scope.ServiceProvider.GetRequiredService<IInstalmentSaleService>().RaiseDueAsync(companyId, new DateOnly(2026, 3, 31))).IsSuccess.Should().BeTrue();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code, DateOnly asAt, params (string Name, object? Value)[] extra)
        {
            Dictionary<string, object?> parameters = new() { ["AsAtDate"] = asAt };
            foreach ((string name, object? value) in extra)
            {
                parameters[name] = value;
            }

            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        string movement = await Render("H17", new DateOnly(2026, 4, 30), ("StandsAccountCode", "1460"));
        movement.Should().Contain("Opening stock 2026-01-01,0,0.00").And.Contain("+ Created by subdivision,2,\"10,000.00\"").And.Contain("− Sold,-1,\"-5,000.00\"")
            .And.Contain("Closing stock 2026-04-30,1,\"5,000.00\"").And.Contain("Stands control account 1460,-,\"5,000.00\"").And.Contain("Difference (register − ledger),-,0.00");
        (await Render("H48", new DateOnly(2026, 1, 31))).Should().Contain($"{scheme.Code},2026-01-10,Ledger receipt,USD,-,1,");
        (await Render("H94", new DateOnly(2026, 4, 30))).Should().Contain("Stand sales,0.00,-,13000");
        string ward = await Render("H97", new DateOnly(2026, 4, 30), ("Ward", "W7"));
        ward.Should().Contain($"Ward W7,Schemes,{scheme.Code}").And.Contain("Ward W7,Stands,2").And.Contain("Ward W7,Stands sold,1").And.Contain("Ward W7,Instalment arrears,\"2,132.38\"");
        (await Render("H95", new DateOnly(2026, 3, 31))).Should().Contain("Collection efficiency, 2026-03");
    }
}
