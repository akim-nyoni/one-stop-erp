using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Onboarding;
using One4All.Core.Housing;
using One4All.Core.Inventory;
using One4All.Core.Municipal;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers migrating the registers that carry history or price structures: a stand's ownership chain, waiting-list applicants with their legacy numbers,
/// municipal tariffs with stepped blocks, and price lists — previewed first, written only when clean, and never loaded twice.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class RegisterHistoryImportTests
{
    private readonly AccountingFixture _fixture;

    public RegisterHistoryImportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Ownership_history_applicants_tariffs_and_price_lists_migrate_from_their_files()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        LandParcel parcel = new(companyId, "RH-P1", "Migrated parcel", 1000m, 5000m);
        db.LandParcels.Add(parcel);
        Item widget = new(companyId, "RH-W", "Widget");
        db.Items.Add(widget);
        await db.SaveChangesAsync();
        db.HousingStands.Add(new HousingStand(companyId, parcel.Id, null, "RH-1", 300m, 1500m, new DateOnly(2010, 1, 1)));
        await db.SaveChangesAsync();
        IVerticalImportService imports = scope.ServiceProvider.GetRequiredService<IVerticalImportService>();

        // Ownership history: joins the chain in date order and stays tamper-evident; a stand with history already is skipped.
        (await imports.ImportOwnershipHistoryAsync(companyId, "stand,date,change,holder\nRH-1,2012-03-01,Origination,Council\n", commit: true)).Errors.Should().ContainSingle();
        string history = "stand,date,change,holder,consideration\nRH-1,2018-06-30,Sale,Tendai Moyo,12000\nRH-1,2012-03-01,Allocation,Tendai Moyo,0\n";
        ImportReport preview = await imports.ImportOwnershipHistoryAsync(companyId, history, commit: false);
        (preview.Rows, preview.Committed, preview.Errors.Count).Should().Be((2, false, 0));
        (await imports.ImportOwnershipHistoryAsync(companyId, history, commit: true)).Created.Should().Be(2);
        HousingStand stand = await db.HousingStands.AsNoTracking().Include(s => s.Ownership).SingleAsync(s => s.CompanyId == companyId && s.StandNumber == "RH-1");
        stand.Ownership.Select(o => o.ChangeType).Should().Equal(OwnershipChangeType.Origination, OwnershipChangeType.Allocation, OwnershipChangeType.Sale);
        stand.VerifyOwnershipChain().Should().BeTrue();
        (await imports.ImportOwnershipHistoryAsync(companyId, history, commit: true)).Skipped.Should().Be(2, "the stand already has its history");

        // Applicants keep their legacy numbers and seniority.
        string applicants = "id_number,name,number,registered,category,income,dependants,marital,regime,spouse_name\n63-111111A11,Rudo One,WL-0001,2015-02-10,MediumDensity,450,2,Married,InCommunity,Tendai One\n"
            + "63-222222B22,Chipo Two,WL-0002,2016-05-01,,300,0,,,\n";
        (await imports.ImportApplicantsAsync(companyId, applicants, commit: true)).Created.Should().Be(2);
        HousingApplicant rudo = await db.HousingApplicants.AsNoTracking().SingleAsync(a => a.CompanyId == companyId && a.IdNumber == "63-111111A11");
        (rudo.WaitingListNumber, rudo.RegistrationDate, rudo.Category, rudo.MonthlyIncome, rudo.Dependants, rudo.MaritalStatus, rudo.MaritalRegime, rudo.SpouseName)
            .Should().Be(("WL-0001", new DateOnly(2015, 2, 10), HousingCategory.MediumDensity, 450m, 2, MaritalStatus.Married, MaritalRegime.InCommunity, "Tendai One"));
        (await imports.ImportApplicantsAsync(companyId, applicants, commit: true)).Skipped.Should().Be(2);
        (await imports.ImportApplicantsAsync(companyId, "id_number,name,number,registered\n63-333333C33,Taken Number,WL-0001,2017-01-01\n", commit: true)).Errors.Should().ContainSingle();

        // Tariffs with stepped blocks; an unknown revenue account is refused.
        string tariffs = "code,name,service,type,effective,revenue_account,fixed,blocks\nRH-WAT,Water stepped,Water,Stepped,2026-01-01,4000,5,0-10:0.5;10-30:0.8;30-:1.2\nRH-REF,Refuse,Refuse,Flat,2026-01-01,4000,12,\n";
        (await imports.ImportTariffsAsync(companyId, tariffs.Replace(",4000,5,", ",9999,5,", StringComparison.Ordinal), commit: true)).Errors.Should().ContainSingle(e => e.Message.Contains("9999", StringComparison.Ordinal));
        (await imports.ImportTariffsAsync(companyId, tariffs, commit: true)).Created.Should().Be(2);
        Tariff water = await db.Tariffs.AsNoTracking().Include(t => t.Blocks).SingleAsync(t => t.CompanyId == companyId && t.Code == "RH-WAT");
        (water.TariffType, water.FixedCharge, water.Blocks.Count, water.Blocks[^1].ToUnit, water.Blocks[^1].Rate).Should().Be((TariffType.Stepped, 5m, 3, null, 1.2m));
        (await imports.ImportTariffsAsync(companyId, tariffs, commit: true)).Skipped.Should().Be(2);

        // Price lists: the list is created from its first row; prices already there are skipped.
        string prices = "price_list,name,item,price,min_qty\nRH-RETAIL,Retail,RH-W,10,1\nRH-RETAIL,,RH-W,9,12\n";
        (await imports.ImportPriceListsAsync(companyId, prices.Replace("RH-W,9", "RH-X,9", StringComparison.Ordinal), commit: true)).Errors.Should().ContainSingle(e => e.Message.Contains("RH-X", StringComparison.Ordinal));
        (await imports.ImportPriceListsAsync(companyId, prices, commit: true)).Created.Should().Be(2);
        PriceList retail = await db.PriceLists.AsNoTracking().Include(p => p.Lines).SingleAsync(p => p.CompanyId == companyId && p.Code == "RH-RETAIL");
        (retail.Name, retail.Currency, retail.Lines.Count, retail.Lines.Single(l => l.MinimumQuantity == 12m).UnitPrice).Should().Be(("Retail", Scenario.Usd, 2, 9m));
        (await imports.ImportPriceListsAsync(companyId, prices, commit: true)).Skipped.Should().Be(2);
    }
}
