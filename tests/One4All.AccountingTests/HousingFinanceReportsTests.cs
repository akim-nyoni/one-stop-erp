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
/// Covers the housing debtors, finance and control reports (H44–H95): an instalment sale's amortisation schedule, its
/// raised instalments aged and in arrears; a tenant's deposit and unpaid rent aged by unit type; repair cost per unit split
/// between council and recharge; and the dashboard's counts.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class HousingFinanceReportsTests
{
    private readonly AccountingFixture _fixture;

    public HousingFinanceReportsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Debtor_finance_and_control_reports_reflect_the_ledgers()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        db.Accounts.Add(new LedgerAccount(companyId, "4965", "Housing Interest Income", AccountType.Income));
        db.Accounts.Add(new LedgerAccount(companyId, "2475", "Tenant Deposits", AccountType.Liability));
        db.Accounts.Add(new LedgerAccount(companyId, "4976", "Repair Recharges", AccountType.Income));
        db.Accounts.Add(new LedgerAccount(companyId, "6960", "Housing Repairs", AccountType.Expense));
        Customer buyer = new(companyId, "HFR-B", "Finance Buyer", Scenario.Usd);
        Customer tenant = new(companyId, "HFR-T", "Finance Tenant", Scenario.Usd);
        db.Customers.AddRange(buyer, tenant);
        await db.SaveChangesAsync();

        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
        long parcelId = (await housing.AcquireLandAsync(companyId, $"LP-HFR-{companyId}", "Parcel", 1000m, 100000m, new DateOnly(2026, 1, 2), "1450", "1100", "LAND-HFR")).Value.LandParcelId;
        long standId = (await housing.SubdivideAsync(companyId, parcelId, null, [new NewStand("HFR-1", 500m), new NewStand("HFR-2", 500m)],
            ApportionMethod.Equal, new DateOnly(2026, 1, 5), "1450", "1460")).Value.StandIds[0];
        IInstalmentSaleService sales = scope.ServiceProvider.GetRequiredService<IInstalmentSaleService>();
        InstalmentSaleResult sale = (await sales.SignAsync(new InstalmentSaleRequest
        {
            CompanyId = companyId, StandId = standId, BuyerCustomerId = buyer.Id, CashPrice = 13000m, Deposit = 1000m, AnnualRatePercent = 12m, Method = InterestMethod.ReducingBalance,
            TermMonths = 12, SignedOn = new DateOnly(2026, 1, 10), FirstDueDate = new DateOnly(2026, 2, 1), SalesAccountCode = "4100", StandsAvailableAccountCode = "1460",
            CostOfSalesAccountCode = "5100", InterestAccountCode = "4965", BankAccountCode = "1100", Tenure = InstalmentTenure.RentToBuy,
        })).Value;
        (await sales.RaiseDueAsync(companyId, new DateOnly(2026, 3, 31))).IsSuccess.Should().BeTrue();

        IRentalHousingService rentals = scope.ServiceProvider.GetRequiredService<IRentalHousingService>();
        (await rentals.AddUnitAsync(companyId, "HFR-F1", "Flat", RentalUnitType.Flat, 2, 300m, "4400", new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();
        (await rentals.LetAsync(new LetUnitRequest
        {
            CompanyId = companyId, UnitCode = "HFR-F1", TenantCustomerId = tenant.Id, StartDate = new DateOnly(2026, 2, 1), Deposit = 600m, BankAccountCode = "1100", DepositAccountCode = "2475",
        })).IsSuccess.Should().BeTrue();
        (await rentals.RunRentAsync(companyId, new DateOnly(2026, 2, 1))).IsSuccess.Should().BeTrue();
        IRentalMaintenanceService maintenance = scope.ServiceProvider.GetRequiredService<IRentalMaintenanceService>();
        string geyser = (await maintenance.ReportRepairAsync(companyId, "HFR-F1", new DateOnly(2026, 3, 2), "Burst geyser")).Value;
        string window = (await maintenance.ReportRepairAsync(companyId, "HFR-F1", new DateOnly(2026, 3, 5), "Broken window")).Value;
        RepairCompletion Complete(string number, decimal cost, bool tenantCaused) => new()
        {
            CompanyId = companyId, RepairNumber = number, Date = new DateOnly(2026, 3, 10), Cost = cost, ExpenseAccountCode = "6960", PaidFromAccountCode = "1100",
            TenantCaused = tenantCaused, RechargeAccountCode = "4976",
        };
        (await maintenance.CompleteRepairAsync(Complete(geyser, 450m, false))).IsSuccess.Should().BeTrue();
        (await maintenance.CompleteRepairAsync(Complete(window, 120m, true))).IsSuccess.Should().BeTrue();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code, string? agreement = null)
        {
            Dictionary<string, object?> parameters = new() { ["AsAtDate"] = new DateOnly(2026, 5, 15) };
            if (agreement is not null)
            {
                parameters["AgreementNumber"] = agreement;
            }

            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        string schedule = await Render("H44", sale.AgreementNumber);
        schedule.Should().Contain("1066.19").And.Contain("Raised").And.Contain("Future").And.Contain("2026-02-01");
        string ageing = await Render("H45");
        ageing.Should().Contain("Rent-to-buy,").And.Contain("Finance Buyer").And.Contain("\"10,098.16\",0.00,0.00,\"1,066.19\",\"1,066.19\"").And.NotContain("Finance Tenant"); // unbilled principal current; Mar 75 days, Feb 103 days
        (await Render("H46")).Should().Contain(sale.AgreementNumber).And.Contain(",2,2026-01-10,2132.38"); // two unpaid instalments; the deposit was the last payment
        string rent = await Render("H75");
        rent.Should().Contain("Flat").And.Contain("Finance Tenant").And.NotContain("Finance Buyer");
        (await Render("H78")).Should().Contain("HFR-F1").And.Contain("600.00");
        (await Render("H79")).Should().Contain("HFR-F1").And.Contain("120.00").And.Contain("450.00").And.Contain("570.00");
        string dashboard = await Render("H95");
        dashboard.Should().Contain("Stands available").And.Contain("2,132.38");
    }
}
