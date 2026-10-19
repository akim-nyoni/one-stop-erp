using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Municipal;
using One4All.Core.Housing;
using One4All.Core.Municipal;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>A fault in a council rental unit is a service desk ticket that follows the unit and raises the unit's repair.</summary>
[Collection(AccountingCollection.Name)]
public sealed class HousingFaultTests
{
    private readonly AccountingFixture _fixture;

    public HousingFaultTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_housing_fault_raises_the_units_repair()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new Core.Accounting.LedgerAccount(companyId, "4975", "Rental income", Core.Accounting.AccountType.Income));
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IRentalHousingService>().AddUnitAsync(companyId, "HF-1", "Council flat", RentalUnitType.House, 2, 200m, "4975", new DateOnly(2026, 1, 1)))
            .IsSuccess.Should().BeTrue();
        IServiceDeskService desk = scope.ServiceProvider.GetRequiredService<IServiceDeskService>();
        (await desk.DefineCategoryAsync(companyId, "HF-PLUMB", "Plumbing", null, 4m, 48m)).IsSuccess.Should().BeTrue();
        FaultReport Report(string? unit) => new()
        {
            CompanyId = companyId, CategoryCode = "HF-PLUMB", RentalUnitCode = unit, ReporterName = "Tenant", ReporterContact = "0771 000 000", Description = "Burst pipe in the kitchen",
            ReportedAt = new DateTimeOffset(2026, 3, 2, 8, 0, 0, TimeSpan.Zero),
        };

        (await desk.LogAsync(Report("NOPE"))).Error.Code.Should().Be("Svc.RentalUnitNotFound");
        string reference = (await desk.LogAsync(Report("hf-1"))).Value;
        ServiceTicket ticket = await db.Set<ServiceTicket>().AsNoTracking().SingleAsync(t => t.CompanyId == companyId && t.Reference == reference);
        ticket.RentalUnitId.Should().NotBeNull();
        ticket.RepairNumber.Should().NotBeNullOrEmpty();
        (await scope.ServiceProvider.GetRequiredService<IRentalMaintenanceService>().HistoryAsync(companyId, "HF-1")).Value.RepairsOpen.Should().Be(1);
    }
}
