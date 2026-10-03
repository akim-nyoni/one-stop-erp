using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the valuation roll: rates at 0.1 cents in the dollar bill a 100,000 stand 100 a month; a supplementary value of
/// 160,000 from February back-bills February and March (120) and April bills 160; an objection upheld at 130,000 replaces
/// the supplementary value from February and credits three months (90), and May bills 130; a general roll for 2027 sets
/// next year's values; the objections register shows the decision.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ValuationRollTests
{
    private readonly AccountingFixture _fixture;

    public ValuationRollTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Supplementary_values_and_objections_adjust_rates_already_billed()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "4980", "Property Rates", AccountType.Income));
        Tariff rates = new(companyId, "RATES", "Property Rates", ServiceType.Rates, TariffType.RatePerValuation, new DateOnly(2026, 1, 1), "4980") { RatePerValuationCents = 0.1m };
        db.Tariffs.Add(rates);
        Customer owner = new(companyId, "VR-OWN", "Stand Owner", Scenario.Usd);
        db.Customers.Add(owner);
        await db.SaveChangesAsync();
        Property extended = new(companyId, "401", "Ward 5", owner.Id) { Valuation = 100_000m };
        extended.AddService(ServiceType.Rates, rates.Id, new DateOnly(2026, 1, 1));
        Property other = new(companyId, "402", "Ward 5", owner.Id) { Valuation = 50_000m };
        other.AddService(ServiceType.Rates, rates.Id, new DateOnly(2026, 1, 1));
        db.Properties.AddRange(extended, other);
        await db.SaveChangesAsync();

        IMunicipalBillingService billing = scope.ServiceProvider.GetRequiredService<IMunicipalBillingService>();
        async Task<decimal> Bill(int month) => (await billing.RunFixedChargeBillingAsync(companyId, ServiceType.Rates, new DateOnly(2026, month, 15), "RT")).Value.TotalBilled;
        foreach (int month in new[] { 1, 2, 3 })
        {
            (await Bill(month)).Should().Be(150m);
        }

        IValuationRollService roll = scope.ServiceProvider.GetRequiredService<IValuationRollService>();
        (await roll.RecordSupplementaryAsync(companyId, extended.Id, new DateOnly(2026, 2, 1), 160_000m, " ", new DateOnly(2026, 3, 20))).Error.Code.Should().Be("Mun.SupplementaryReason");
        ValuationAdjustment supplementary = (await roll.RecordSupplementaryAsync(companyId, extended.Id, new DateOnly(2026, 2, 1), 160_000m, "Second storey added", new DateOnly(2026, 3, 20))).Value;
        supplementary.BillsAdjusted.Should().Be(2);
        supplementary.Amount.Should().Be(120m);
        (await Bill(4)).Should().Be(210m);

        string objection = (await roll.LodgeObjectionAsync(companyId, extended.Id, new DateOnly(2026, 4, 10), 120_000m, "Comparable sales are lower")).Value;
        (await roll.LodgeObjectionAsync(companyId, extended.Id, new DateOnly(2026, 4, 11), 110_000m, "Again")).Error.Code.Should().Be("Mun.ObjectionOpen");
        (await roll.DecideObjectionAsync(companyId, objection, new DateOnly(2026, 5, 5), 130_000m, "Partly upheld on the comparables")).Value
            .Should().Be(new ValuationAdjustment(3, -90m, objection + "-ADJ"));
        (await roll.DecideObjectionAsync(companyId, objection, new DateOnly(2026, 5, 6), null, "Again")).Error.Code.Should().Be("Mun.ObjectionDecided");
        (await Bill(5)).Should().Be(180m);

        (await roll.ImportRollAsync(companyId, "GV2027", new DateOnly(2027, 1, 1), [new RollEntry("401", 140_000m), new RollEntry("999", 1m)])).Error.Code.Should().Be("Mun.UnknownStand");
        (await roll.ImportRollAsync(companyId, "GV2027", new DateOnly(2027, 1, 1), [new RollEntry("401", 140_000m), new RollEntry("402", 60_000m)])).Value.Should().Be(2);
        (await db.Properties.AsNoTracking().SingleAsync(p => p.Id == extended.Id)).Valuation.Should().Be(140_000m);

        (await roll.HistoryAsync(companyId, extended.Id)).Should().Equal(
            new ValuationEntry(DateOnly.MinValue, 100_000m, ValuationKind.General, "OPENING"),
            new ValuationEntry(new DateOnly(2026, 2, 1), 160_000m, ValuationKind.Supplementary, supplementary.DocumentNumber![..^4]),
            new ValuationEntry(new DateOnly(2026, 2, 1), 130_000m, ValuationKind.Objection, objection),
            new ValuationEntry(new DateOnly(2027, 1, 1), 140_000m, ValuationKind.General, "GV2027"));
        (await roll.ObjectionsAsync(companyId, null)).Should().Equal(
            new ObjectionEntry(objection, "401", new DateOnly(2026, 4, 10), 160_000m, 120_000m, ObjectionStatus.Upheld, 130_000m));
        (await roll.ObjectionsAsync(companyId, ObjectionStatus.Lodged)).Should().BeEmpty();

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 5, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Single(r => r.AccountCode == "4980").Credit.Should().Be(870m);
    }
}
