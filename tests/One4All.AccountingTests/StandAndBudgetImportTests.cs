using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Onboarding;
using One4All.Core.Budgeting;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers migrating the housing stand register and a budget: stands come in on their parcels (a parcel not yet registered is made from its stands), sold
/// statuses are refused, and a budget file becomes a budget for the year of the date — previewed first, written only when clean.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class StandAndBudgetImportTests
{
    private readonly AccountingFixture _fixture;

    public StandAndBudgetImportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Stands_and_a_budget_migrate_from_their_files()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        HousingScheme scheme = new(companyId, $"SB-{companyId}", "Migrated scheme", SchemeType.CouncilDelivered);
        db.HousingSchemes.Add(scheme);
        await db.SaveChangesAsync();
        IVerticalImportService imports = scope.ServiceProvider.GetRequiredService<IVerticalImportService>();

        string bad = "stand,parcel,size,status\nSB-1,PX-1,300,Sold\n";
        (await imports.ImportStandsAsync(companyId, bad, commit: true)).Errors.Should().ContainSingle();

        string stands = $"stand,parcel,size,cost,scheme,status\nSB-1,PX-1,300,1500,SB-{companyId},Serviced\nSB-2,PX-1,200,1000,,\nSB-3,PX-2,500,2500,,Available\n";
        ImportReport preview = await imports.ImportStandsAsync(companyId, stands, commit: false);
        (preview.Rows, preview.Committed, preview.Errors.Count).Should().Be((3, false, 0));
        ImportReport written = await imports.ImportStandsAsync(companyId, stands, commit: true);
        (written.Created, written.Committed).Should().Be((3, true));
        LandParcel px1 = await db.LandParcels.AsNoTracking().SingleAsync(p => p.CompanyId == companyId && p.ParcelNumber == "PX-1");
        (px1.AreaSquareMetres, px1.AcquisitionCost).Should().Be((500m, 2500m));
        (await db.HousingStands.AsNoTracking().SingleAsync(s => s.CompanyId == companyId && s.StandNumber == "SB-1")).Status.Should().Be(StandStatus.Serviced);
        (await imports.ImportStandsAsync(companyId, stands, commit: true)).Skipped.Should().Be(3, "they are on the register already");

        string budget = "account,period,amount\n6800,1,100\n6800,2,150\n9999,1,10\n";
        (await imports.ImportBudgetAsync(companyId, budget, new DateOnly(2026, 6, 1), "Migrated 2026", BudgetType.OriginalApproved, null, commit: true)).Errors
            .Should().ContainSingle(e => e.Message.Contains("9999", StringComparison.Ordinal));
        ImportReport loaded = await imports.ImportBudgetAsync(companyId, budget.Replace("9999,1,10\n", string.Empty, StringComparison.Ordinal), new DateOnly(2026, 6, 1), "Migrated 2026",
            BudgetType.OriginalApproved, null, commit: true);
        (loaded.Created, loaded.Committed).Should().Be((2, true));
        Budget made = await db.Budgets.AsNoTracking().Include(b => b.Lines).SingleAsync(b => b.CompanyId == companyId && b.Label == "Migrated 2026");
        made.Lines.Sum(l => l.Amount).Should().Be(250m);
    }
}
