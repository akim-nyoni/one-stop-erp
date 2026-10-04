using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers IAS 37 compensation on compulsory acquisition: two claims on a parcel bought for 100,000 (land WIP). The first
/// is lodged (nothing provided), provided at 30,000 when probable, remeasured to 36,000, and settled for 35,000 — the
/// 1,000 over-provision comes out of the land cost; the second is provided at 8,000 and withdrawn, releasing it. Land WIP
/// ends at 135,000, the provision at nothing, the bank down 135,000 in all.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class CompensationTests
{
    private readonly AccountingFixture _fixture;

    public CompensationTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Compensation_is_provided_when_probable_and_capitalised_into_land()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "2280", "Compensation Provisions", AccountType.Liability));
        await db.SaveChangesAsync();

        long parcelId = (await scope.ServiceProvider.GetRequiredService<IHousingService>().AcquireLandAsync(companyId, $"LP-CP-{companyId}", "Acquired farm", 20000m, 100000m,
            new DateOnly(2026, 1, 10), "1450", "1100", "LAND-CP")).Value.LandParcelId;

        ICompensationService compensation = scope.ServiceProvider.GetRequiredService<ICompensationService>();
        string farmer = (await compensation.LodgeAsync(companyId, parcelId, "Former occupier", new DateOnly(2026, 2, 1), "1450", "2280")).Value;
        string tenant = (await compensation.LodgeAsync(companyId, parcelId, "Grazing tenant", new DateOnly(2026, 2, 1), "1450", "2280")).Value;

        (await compensation.ProvideAsync(companyId, farmer, 30000m, new DateOnly(2026, 3, 31))).Value.Should().Be(30000m);
        (await compensation.ProvideAsync(companyId, farmer, 36000m, new DateOnly(2026, 6, 30))).Value.Should().Be(36000m);
        (await compensation.SettleAsync(companyId, farmer, 35000m, "1100", new DateOnly(2026, 8, 15))).IsSuccess.Should().BeTrue();
        (await compensation.ProvideAsync(companyId, farmer, 1000m, new DateOnly(2026, 8, 16))).Error.Code.Should().Be("Hse.ClaimClosed");

        (await compensation.ProvideAsync(companyId, tenant, 8000m, new DateOnly(2026, 3, 31))).IsSuccess.Should().BeTrue();
        (await compensation.WithdrawAsync(companyId, tenant, new DateOnly(2026, 9, 1))).IsSuccess.Should().BeTrue();

        (await compensation.RegisterAsync(companyId)).Select(c => (c.ClaimNumber, c.Status, c.Provision, c.SettledAmount)).Should().Equal(
            (farmer, CompensationStatus.Settled, 0m, (decimal?)35000m), (tenant, CompensationStatus.Withdrawn, 0m, (decimal?)null));

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 12, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        decimal Net(string code) => tb.Rows.Where(r => r.AccountCode == code).Sum(r => r.Debit - r.Credit);
        Net("1450").Should().Be(135000m);
        Net("2280").Should().Be(0m);
        Net("1100").Should().Be(-135000m);
    }
}
