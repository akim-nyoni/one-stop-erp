using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the ownership, title and control housing reports (H49, H56–H70): a paid-up holder appears ready for transfer until
/// title is applied for; the chain of title verifies; an open transfer blocked by the transferor's arrears shows what is
/// needed; the title pipeline and caveat register list their entries; and the broken-chain report stays empty.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class HousingTitleReportsTests
{
    private readonly AccountingFixture _fixture;

    public HousingTitleReportsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Ownership_title_and_control_reports_reflect_the_registers()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        Customer paid = new(companyId, "HT-P", "Paid Holder", Scenario.Usd) { MobileNumber = "+263771000001" };
        Customer owing = new(companyId, "HT-O", "Owing Holder", Scenario.Usd);
        Customer buyer = new(companyId, "HT-B", "Incoming Buyer", Scenario.Usd);
        db.Customers.AddRange(paid, owing, buyer);
        LandParcel parcel = new(companyId, $"LP-HT-{companyId}", "Title parcel", 5000m, 50000m);
        db.LandParcels.Add(parcel);
        await db.SaveChangesAsync();
        HousingStand first = new(companyId, parcel.Id, null, "HT-1", 300m, 1000m);
        HousingStand second = new(companyId, parcel.Id, null, "HT-2", 300m, 1000m);
        db.HousingStands.AddRange(first, second);
        await db.SaveChangesAsync();

        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        (await housing.SellStandAsync(companyId, first.Id, paid.Id, paid.Name, 4000m, new DateOnly(2026, 2, 1), "4100", "1460", "5100", $"HT-I1-{companyId}")).IsSuccess.Should().BeTrue();
        (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = paid.Id, DocumentNumber = $"HT-R1-{companyId}", ReceiptDate = new DateOnly(2026, 2, 5), Currency = Scenario.Usd, Amount = 4000m, BankAccountCode = "1100" })).IsSuccess.Should().BeTrue();
        (await housing.SellStandAsync(companyId, second.Id, owing.Id, owing.Name, 4000m, new DateOnly(2026, 2, 1), "4100", "1460", "5100", $"HT-I2-{companyId}")).IsSuccess.Should().BeTrue();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code, Dictionary<string, object?>? parameters = null)
        {
            var rendered = await runner.RenderAsync(companyId, code, parameters ?? new() { ["AsAtDate"] = new DateOnly(2026, 6, 30) }, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        string ready = await Render("H49");
        ready.Should().Contain("HT-1").And.Contain("Paid Holder").And.NotContain("HT-2");
        string chain = await Render("H56", new() { ["StandNumber"] = "HT-1" });
        chain.Should().Contain("Origination").And.Contain("Sale").And.Contain("Paid Holder").And.Contain("OK").And.NotContain("HT-2");

        // A transfer of the owing holder's stand is blocked by their arrears.
        string transfer = (await scope.ServiceProvider.GetRequiredService<ITransferApplicationService>().LodgeAsync(new TransferLodgement
        {
            CompanyId = companyId, StandId = second.Id, TransferType = TransferType.Sale, ToCustomerId = buyer.Id, Consideration = 6000m, Date = new DateOnly(2026, 6, 1),
        })).Value;
        (await Render("H58")).Should().Contain(transfer).And.Contain("Checklist verification");
        (await Render("H59")).Should().Contain(transfer).And.Contain("HousingAccountClear").And.Contain("owes 4,000.00");

        // Title applied for: no longer "ready", now in the pipeline; a caveat shows in the register.
        (await scope.ServiceProvider.GetRequiredService<ITitleDeedService>().ApplyAsync(companyId, first.Id, new DateOnly(2026, 6, 10))).IsSuccess.Should().BeTrue();
        (await Render("H49")).Should().NotContain("HT-1");
        (await Render("H64")).Should().Contain("HT-1").And.Contain("Applied");
        db.Set<StandBlock>().Add(new StandBlock(companyId, second.Id, BlockReason.FamilyDispute, "Estate dispute", "AFF-3", 1, new DateOnly(2026, 6, 20)));
        await db.SaveChangesAsync();
        (await Render("H68")).Should().Contain("HT-2").And.Contain("FamilyDispute").And.Contain("In force");
        (await Render("H69")).Should().NotContain("HT-");
    }
}
