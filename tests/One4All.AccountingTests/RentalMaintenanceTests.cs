using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers rental maintenance: a burst geyser (wear and tear, 450) and a broken window caused by the tenant (120,
/// recharged) on a let flat; a leak reported while another flat was empty cannot be recharged; a repair cannot be
/// completed twice; an inspection graded 2 with a follow-up, then closed; the history shows the costs, recharge, grade
/// and open items. Repairs expense 570, recharge income 120.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class RentalMaintenanceTests
{
    private readonly AccountingFixture _fixture;

    public RentalMaintenanceTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Repairs_are_costed_and_recharged_and_inspections_followed_up()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "4975", "Rental Income", AccountType.Income));
        db.Accounts.Add(new LedgerAccount(companyId, "4976", "Repair Recharges", AccountType.Income));
        db.Accounts.Add(new LedgerAccount(companyId, "6960", "Housing Repairs", AccountType.Expense));
        Customer tenant = new(companyId, "MT-TEN", "Maintenance Tenant", Scenario.Usd);
        db.Customers.Add(tenant);
        await db.SaveChangesAsync();

        IRentalHousingService rentals = scope.ServiceProvider.GetRequiredService<IRentalHousingService>();
        (await rentals.AddUnitAsync(companyId, "MT-1", "Flat 1", RentalUnitType.Flat, 2, 300m, "4975", new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();
        (await rentals.AddUnitAsync(companyId, "MT-2", "Flat 2", RentalUnitType.Flat, 2, 300m, "4975", new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();
        (await rentals.LetAsync(new LetUnitRequest { CompanyId = companyId, UnitCode = "MT-1", TenantCustomerId = tenant.Id, StartDate = new DateOnly(2026, 2, 1) })).IsSuccess.Should().BeTrue();

        IRentalMaintenanceService maintenance = scope.ServiceProvider.GetRequiredService<IRentalMaintenanceService>();
        string geyser = (await maintenance.ReportRepairAsync(companyId, "mt-1", new DateOnly(2026, 3, 2), "Burst geyser")).Value;
        string window = (await maintenance.ReportRepairAsync(companyId, "MT-1", new DateOnly(2026, 3, 5), "Window broken by tenant")).Value;
        string leak = (await maintenance.ReportRepairAsync(companyId, "MT-2", new DateOnly(2026, 3, 6), "Roof leak")).Value;

        RepairCompletion Complete(string number, decimal cost, bool tenantCaused) => new()
        {
            CompanyId = companyId, RepairNumber = number, Date = new DateOnly(2026, 3, 10), Cost = cost, ExpenseAccountCode = "6960", PaidFromAccountCode = "1100",
            TenantCaused = tenantCaused, RechargeAccountCode = "4976",
        };

        (await maintenance.CompleteRepairAsync(Complete(geyser, 450m, false))).Value.Should().BeNull();
        (await maintenance.CompleteRepairAsync(Complete(window, 120m, true))).Value.Should().Be(window + "-RC");
        (await maintenance.CompleteRepairAsync(Complete(window, 120m, true))).Error.Code.Should().Be("Hse.RepairClosed");
        (await maintenance.CompleteRepairAsync(Complete(leak, 80m, true))).Error.Code.Should().Be("Hse.NoTenant");

        (await maintenance.RecordInspectionAsync(companyId, "MT-1", new DateOnly(2026, 4, 1), "P. Dube", 6, "Fine", false)).Error.Code.Should().Be("Hse.InspectionDetails");
        long inspection = (await maintenance.RecordInspectionAsync(companyId, "MT-1", new DateOnly(2026, 4, 1), "P. Dube", 2, "Damp in bedroom", true)).Value;
        (await maintenance.HistoryAsync(companyId, "MT-1")).Value.Should().Be(new UnitMaintenanceHistory("MT-1", 0, 570m, 120m, 2, 1));
        (await maintenance.CloseFollowUpAsync(companyId, inspection, new DateOnly(2026, 4, 20))).IsSuccess.Should().BeTrue();
        (await maintenance.CloseFollowUpAsync(companyId, inspection, new DateOnly(2026, 4, 21))).Error.Code.Should().Be("Hse.NoFollowUp");
        (await maintenance.HistoryAsync(companyId, "MT-1")).Value.FollowUpsOpen.Should().Be(0);
        (await maintenance.HistoryAsync(companyId, "MT-2")).Value.RepairsOpen.Should().Be(1);

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 4, 30))).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Single(r => r.AccountCode == "6960").Debit.Should().Be(570m);
        tb.Rows.Single(r => r.AccountCode == "4976").Credit.Should().Be(120m);
    }
}
