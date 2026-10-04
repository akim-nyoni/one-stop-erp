using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers development levies: allocation levies (flat, per m², % of the price frozen at offer) are raised on the
/// beneficiary's account once the allocation is accepted, never twice, and not at all for a deactivated levy or a
/// beneficiary with no account; transfer levies are raised on the incoming holder on the consideration, and not on a
/// rejected transfer.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class DevelopmentLevyTests
{
    private readonly AccountingFixture _fixture;

    public DevelopmentLevyTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Levies_are_raised_once_per_allocation_and_transfer()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        string beneficiaryId = $"LV-A-{companyId}";
        Customer beneficiary = new(companyId, "LV-BEN", "Levy Beneficiary", Scenario.Usd) { NationalIdNumber = beneficiaryId };
        Customer seller = new(companyId, "LV-SEL", "Levy Seller", Scenario.Usd);
        Customer buyer = new(companyId, "LV-BUY", "Levy Buyer", Scenario.Usd);
        db.Customers.AddRange(beneficiary, seller, buyer);
        LandParcel parcel = new(companyId, $"LP-LV-{companyId}", "Levy parcel", 5000m, 50000m);
        HousingScheme scheme = new(companyId, $"SCH-LV-{companyId}", "Levy Scheme", SchemeType.CouncilDelivered);
        db.LandParcels.Add(parcel);
        db.HousingSchemes.Add(scheme);
        await db.SaveChangesAsync();
        HousingStand first = new(companyId, parcel.Id, scheme.Id, "LV-1", 300m, 10000m);
        HousingStand second = new(companyId, parcel.Id, scheme.Id, "LV-2", 300m, 10000m);
        db.HousingStands.AddRange(first, second);
        StandPricingRule price = new(companyId, PricingScope.Scheme, scheme.Id, null, null, null, new DateOnly(2026, 1, 1), 0m, 0m, 0m, 0m, 0m, 1);
        price.Approve(2, new DateOnly(2026, 1, 1));
        db.Set<StandPricingRule>().Add(price);
        await db.SaveChangesAsync();

        IDevelopmentLevyService levies = scope.ServiceProvider.GetRequiredService<IDevelopmentLevyService>();
        (await levies.DefineAsync(companyId, "endow", "Endowment fee", LevyTrigger.Allocation, LevyBasis.Flat, 250m, "4400")).IsSuccess.Should().BeTrue();
        (await levies.DefineAsync(companyId, "INFRA", "Infrastructure development levy", LevyTrigger.Allocation, LevyBasis.PerSquareMetre, 2m, "4400")).IsSuccess.Should().BeTrue();
        (await levies.DefineAsync(companyId, "LANDVAL", "Intrinsic land value charge", LevyTrigger.Allocation, LevyBasis.PercentOfPrice, 5m, "4400")).IsSuccess.Should().BeTrue();
        (await levies.DefineAsync(companyId, "OLD", "Retired charge", LevyTrigger.Allocation, LevyBasis.Flat, 999m, "4400")).IsSuccess.Should().BeTrue();
        (await levies.DefineAsync(companyId, "ENDOW", "Duplicate", LevyTrigger.Allocation, LevyBasis.Flat, 1m, "4400")).Error.Code.Should().Be("Hse.LevyExists");
        (await levies.DefineAsync(companyId, "BAD", "Over 100%", LevyTrigger.Transfer, LevyBasis.PercentOfPrice, 120m, "4400")).Error.Code.Should().Be("Hse.Levy");
        (await levies.DeactivateAsync(companyId, "OLD")).IsSuccess.Should().BeTrue();
        (await levies.DefineAsync(companyId, "XFER", "Transfer levy", LevyTrigger.Transfer, LevyBasis.PercentOfPrice, 2m, "4400")).IsSuccess.Should().BeTrue();
        (await levies.DefineAsync(companyId, "CONN", "Connection fee", LevyTrigger.Transfer, LevyBasis.Flat, 100m, "4400")).IsSuccess.Should().BeTrue();

        // Two beneficiaries offered stands at the frozen price of 10,000; only the first has a customer account.
        IWaitingListService list = scope.ServiceProvider.GetRequiredService<IWaitingListService>();
        foreach ((string id, int year) in new[] { (beneficiaryId, 2015), ($"LV-B-{companyId}", 2016) })
        {
            (await list.RegisterAsync(new WaitingListApplication
            {
                CompanyId = companyId, IdNumber = id, FullName = id, Category = HousingCategory.HighDensity, DateOfBirth = new DateOnly(1980, 1, 1),
                ResidentSince = new DateOnly(2000, 1, 1), MonthlyIncome = 500m, Tenure = CurrentTenure.Tenant, RegistrationDate = new DateOnly(year, 1, 1),
            })).IsSuccess.Should().BeTrue();
        }

        IAllocationService allocations = scope.ServiceProvider.GetRequiredService<IAllocationService>();
        AllocationRunView run = (await allocations.ProposeAsync(new AllocationProposal
        {
            CompanyId = companyId, SchemeId = scheme.Id, Category = HousingCategory.HighDensity, Method = AllocationMethod.Seniority, AsAtDate = new DateOnly(2026, 3, 1), UserId = 71,
        })).Value;
        AllocationRunView offered = (await allocations.ApproveAsync(companyId, run.RunNumber, 72, new DateOnly(2026, 3, 1), 30)).Value;
        string certificate = offered.Lines[0].CertificateNumber!;
        string other = offered.Lines[1].CertificateNumber!;

        DateOnly charged = new(2026, 3, 10);
        (await levies.ChargeAllocationAsync(companyId, certificate, charged)).Error.Code.Should().Be("Hse.NotAccepted");
        (await allocations.AcceptAsync(companyId, certificate, new DateOnly(2026, 3, 5))).IsSuccess.Should().BeTrue();
        (await allocations.AcceptAsync(companyId, other, new DateOnly(2026, 3, 5))).IsSuccess.Should().BeTrue();
        IReadOnlyList<LevyRaised> raised = (await levies.ChargeAllocationAsync(companyId, certificate, charged)).Value;
        raised.Select(r => (r.LevyCode, r.Amount)).Should().Equal(("ENDOW", 250m), ("INFRA", 600m), ("LANDVAL", 500m));
        (await levies.ChargeAllocationAsync(companyId, certificate, charged)).Value.Should().BeEmpty();
        (await levies.ChargeAllocationAsync(companyId, other, charged)).Error.Code.Should().Be("Hse.NoCustomerAccount");
        (await db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.TraderId == beneficiary.Id).SumAsync(i => i.OutstandingTransaction)).Should().Be(1350m);

        // A transfer's levies fall on the incoming holder, on the consideration; a rejected transfer raises none.
        TransferApplication sale = new(companyId, $"TRF-LV1-{companyId}", first.Id, TransferType.Sale, seller.Id, buyer.Id, 20000m, new DateOnly(2026, 6, 1));
        TransferApplication refused = new(companyId, $"TRF-LV2-{companyId}", second.Id, TransferType.Sale, seller.Id, buyer.Id, 15000m, new DateOnly(2026, 6, 1));
        refused.Reject(new DateOnly(2026, 6, 2), "Incomplete");
        db.Set<TransferApplication>().AddRange(sale, refused);
        await db.SaveChangesAsync();
        (await levies.ChargeTransferAsync(companyId, sale.ApplicationNumber, new DateOnly(2026, 6, 3))).Value.Select(r => (r.LevyCode, r.Amount)).Should().Equal(("CONN", 100m), ("XFER", 400m));
        (await levies.ChargeTransferAsync(companyId, sale.ApplicationNumber, new DateOnly(2026, 6, 4))).Value.Should().BeEmpty();
        (await levies.ChargeTransferAsync(companyId, refused.ApplicationNumber, new DateOnly(2026, 6, 3))).Error.Code.Should().Be("Hse.TransferClosed");
        (await db.Set<LevyCharge>().CountAsync(c => c.CompanyId == companyId)).Should().Be(5);
    }
}
