using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class HousingTests
{
    private readonly AccountingFixture _fixture;

    public HousingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Land_bank_wip_to_sale_flows_through_ias2_inventory_and_ar()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long buyerId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer buyer = new(companyId, "BEN-1", "Beneficiary Family", Scenario.Usd);
            db.Customers.Add(buyer);
            await db.SaveChangesAsync();
            buyerId = buyer.Id;
        }

        long parcelId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
            var acq = await housing.AcquireLandAsync(companyId, "LP-1", "Peri-urban parcel", 10000m, 100000m, new DateOnly(2026, 2, 1), "1450", "1100", "LAND-1");
            acq.IsSuccess.Should().BeTrue(acq.IsFailure ? acq.Error.ToString() : null);
            parcelId = acq.Value.LandParcelId;
        }

        long standId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
            var sub = await housing.SubdivideAsync(companyId, parcelId, null,
                [new NewStand("S-1", 250m), new NewStand("S-2", 250m), new NewStand("S-3", 250m), new NewStand("S-4", 250m)],
                ApportionMethod.Equal, new DateOnly(2026, 3, 1), "1450", "1460");
            sub.IsSuccess.Should().BeTrue(sub.IsFailure ? sub.Error.ToString() : null);
            sub.Value.StandIds.Should().HaveCount(4);
            standId = sub.Value.StandIds[0];
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
            // Each of 4 stands carries 25,000 cost; sell one for 40,000.
            var sale = await housing.SellStandAsync(companyId, standId, buyerId, "Beneficiary Family", 40000m, new DateOnly(2026, 4, 1), "4100", "1460", "5100", "HSINV-1");
            sale.IsSuccess.Should().BeTrue(sale.IsFailure ? sale.Error.ToString() : null);
            sale.Value.CostOfSales.Should().Be(25000m);

            // A cession without a cleared account / approval must be refused; with both, it succeeds.
            var blocked = await housing.TransferOwnershipAsync(companyId, standId, buyerId, "New Holder", 45000m, new DateOnly(2026, 5, 1), housingAccountClear: false, dualApprovalRecorded: true);
            blocked.IsFailure.Should().BeTrue();
            blocked.Error.Code.Should().Be("Housing.AccountNotClear");

            var ceded = await housing.TransferOwnershipAsync(companyId, standId, buyerId, "New Holder", 45000m, new DateOnly(2026, 5, 1), housingAccountClear: true, dualApprovalRecorded: true);
            ceded.IsSuccess.Should().BeTrue(ceded.IsFailure ? ceded.Error.ToString() : null);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            HousingStand stand = await db.Set<HousingStand>().Include(s => s.Ownership).FirstAsync(s => s.Id == standId);
            stand.VerifyOwnershipChain().Should().BeTrue();
            stand.Ownership.Should().HaveCount(3); // origination + sale + cession

            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            AgeingReport debtors = await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 4, 30));
            debtors.Total.Should().Be(40000m);

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 5, 31));
            tb.IsBalanced.Should().BeTrue();
        }
    }
}
