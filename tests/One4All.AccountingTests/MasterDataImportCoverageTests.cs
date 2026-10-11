using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Onboarding;
using One4All.Core.Accounting;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers importing the chart of accounts, warehouses and council rental units: preview first, bad rows refused, existing codes left alone.</summary>
[Collection(AccountingCollection.Name)]
public sealed class MasterDataImportCoverageTests
{
    private readonly AccountingFixture _fixture;

    public MasterDataImportCoverageTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Accounts_warehouses_and_rental_units_import()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IVerticalImportService import = scope.ServiceProvider.GetRequiredService<IVerticalImportService>();

        const string accounts = "code,name,type,category,manual\n4000,Sales,Income,,\n4710,Hall hire,income,Other income,false\n6810,Repairs,Expense,,\n";
        ImportReport preview = await import.ImportAccountsAsync(companyId, accounts, commit: false);
        (preview.Rows, preview.Skipped, preview.Committed, preview.Errors.Count).Should().Be((3, 1, false, 0));
        (await import.ImportAccountsAsync(companyId, accounts + "9999,Bad,Asset-ish,,\n", commit: true)).Errors.Should().ContainSingle(e => e.LineNumber == 5);
        (await import.ImportAccountsAsync(companyId, accounts, commit: true)).Created.Should().Be(2);
        LedgerAccount hall = await db.Accounts.AsNoTracking().SingleAsync(a => a.CompanyId == companyId && a.AccountCode == "4710");
        (hall.Type, hall.AllowManualJournal, hall.BudgetEnabled, hall.IsActive).Should().Be((AccountType.Income, false, true, true));

        (await import.ImportWarehousesAsync(companyId, "code,name\nmimp,Main imports\nMIMP,Again\n", commit: true)).Errors.Should().ContainSingle(e => e.Message.Contains("twice"));
        (await import.ImportWarehousesAsync(companyId, "code,name\nmimp,Main imports\n", commit: true)).Created.Should().Be(1);

        const string units = "code,description,type,rooms,rent,rent_account,vacant_since,suburb,max_occupants\n"
            + "IM-F1,Flat 1,Flat,2,300,4710,2026-01-01,Mabvuku,4\nIM-H1,House 1,house,3,500,4710,,,\n";
        (await import.ImportRentalUnitsAsync(companyId, units.Replace("4710,2026", "4999,2026"), new DateOnly(2026, 6, 1), commit: true)).Errors.Should().ContainSingle(e => e.Message.Contains("4999"));
        (await import.ImportRentalUnitsAsync(companyId, units, new DateOnly(2026, 6, 1), commit: true)).Created.Should().Be(2);
        RentalUnit flat = await db.Set<RentalUnit>().AsNoTracking().SingleAsync(u => u.CompanyId == companyId && u.Code == "IM-F1");
        (flat.UnitType, flat.Rooms, flat.MonthlyRent, flat.VacantSince, flat.Suburb, flat.MaxOccupants).Should().Be((RentalUnitType.Flat, 2, 300m, (DateOnly?)new DateOnly(2026, 1, 1), "Mabvuku", (int?)4));
        (await db.Set<RentalUnit>().AsNoTracking().SingleAsync(u => u.CompanyId == companyId && u.Code == "IM-H1")).IsLet.Should().BeTrue();
    }
}
