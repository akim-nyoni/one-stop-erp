using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Accounting;
using One4All.Core.Municipal;
using One4All.Core.Security;
using One4All.Core.Trading;
using One4All.Core.Workflow;
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
        (await roll.RecordSupplementaryAsync(companyId, extended.Id, new DateOnly(2026, 2, 1), 160_000m, " ", new DateOnly(2026, 3, 20), 900_101)).Error.Code.Should().Be("Mun.SupplementaryReason");
        ValuationAdjustment supplementary = (await roll.RecordSupplementaryAsync(companyId, extended.Id, new DateOnly(2026, 2, 1), 160_000m, "Second storey added", new DateOnly(2026, 3, 20), 900_101)).Value;
        supplementary.BillsAdjusted.Should().Be(2);
        supplementary.Amount.Should().Be(120m);
        (await Bill(4)).Should().Be(210m);

        string objection = (await roll.LodgeObjectionAsync(companyId, extended.Id, new DateOnly(2026, 4, 10), 120_000m, "Comparable sales are lower")).Value;
        (await roll.LodgeObjectionAsync(companyId, extended.Id, new DateOnly(2026, 4, 11), 110_000m, "Again")).Error.Code.Should().Be("Mun.ObjectionOpen");
        (await roll.DecideObjectionAsync(companyId, objection, new DateOnly(2026, 5, 5), 130_000m, "Partly upheld on the comparables", 900_102)).Value
            .Should().Be(new ValuationAdjustment(3, -90m, objection + "-ADJ"));
        (await roll.DecideObjectionAsync(companyId, objection, new DateOnly(2026, 5, 6), null, "Again", 900_102)).Error.Code.Should().Be("Mun.ObjectionDecided");
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

    [SkippableFact]
    public async Task Valuation_changes_record_the_user_go_back_only_so_far_and_hold_large_credits_for_approval()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        string tag = Guid.NewGuid().ToString("N")[..8];
        db.Accounts.Add(new LedgerAccount(companyId, "4980", "Property Rates", AccountType.Income));
        Tariff rates = new(companyId, "RATES", "Property Rates", ServiceType.Rates, TariffType.RatePerValuation, new DateOnly(2026, 1, 1), "4980") { RatePerValuationCents = 0.1m };
        db.Tariffs.Add(rates);
        Customer owner = new(companyId, "VC-OWN", "Stand Owner", Scenario.Usd);
        db.Customers.Add(owner);
        User valuer = new($"val{tag}", "Valuer"), treasurer = new($"trs{tag}", "Treasurer");
        db.AddRange(valuer, treasurer);
        await db.SaveChangesAsync();
        Property demolished = new(companyId, "501", "Ward 6", owner.Id) { Valuation = 100_000m };
        demolished.AddService(ServiceType.Rates, rates.Id, new DateOnly(2026, 1, 1));
        Property small = new(companyId, "502", "Ward 6", owner.Id) { Valuation = 50_000m };
        small.AddService(ServiceType.Rates, rates.Id, new DateOnly(2026, 1, 1));
        db.Properties.AddRange(demolished, small);
        WorkflowDefinition credits = new(companyId, WorkflowDocumentTypes.CreditNote, "Credit notes");
        credits.AddStage(1, "Treasurer").AddEligibleUser(treasurer.Id);
        db.WorkflowDefinitions.Add(credits);
        await db.SaveChangesAsync();

        IMunicipalBillingService billing = scope.ServiceProvider.GetRequiredService<IMunicipalBillingService>();
        foreach (int month in new[] { 1, 2, 3 })
        {
            (await billing.RunFixedChargeBillingAsync(companyId, ServiceType.Rates, new DateOnly(2026, month, 15), "RT")).Value.TotalBilled.Should().Be(150m);
        }

        // By default a supplementary value goes back no further than the start of the financial year; the council may set so many months.
        IValuationRollService roll = scope.ServiceProvider.GetRequiredService<IValuationRollService>();
        (await roll.RecordSupplementaryAsync(companyId, demolished.Id, new DateOnly(2025, 12, 1), 40_000m, "Outbuildings demolished", new DateOnly(2026, 3, 20), valuer.Id))
            .Error.Code.Should().Be("Mun.BackdateLimit");
        (await roll.SetPolicyAsync(companyId, -1, null)).Error.Code.Should().Be("Mun.ValuationPolicy");
        (await roll.SetPolicyAsync(companyId, 1, 50m)).IsSuccess.Should().BeTrue();
        (await roll.RecordSupplementaryAsync(companyId, demolished.Id, new DateOnly(2026, 2, 1), 40_000m, "Outbuildings demolished", new DateOnly(2026, 3, 20), valuer.Id))
            .Error.Code.Should().Be("Mun.BackdateLimit");
        (await roll.SetPolicyAsync(companyId, 3, 50m)).IsSuccess.Should().BeTrue();

        // A credit of 120 is above the threshold of 50: held in the credit-note workflow, not posted; the value is recorded with its valuer.
        ValuationAdjustment large = (await roll.RecordSupplementaryAsync(companyId, demolished.Id, new DateOnly(2026, 2, 1), 40_000m, "Outbuildings demolished", new DateOnly(2026, 3, 20), valuer.Id)).Value;
        (large.BillsAdjusted, large.Amount, large.HeldForApproval).Should().Be((2, -120m, true));
        (await db.OpenItems.AnyAsync(i => i.CompanyId == companyId && i.DocumentNumber == large.DocumentNumber)).Should().BeFalse();
        (await db.Set<PendingCreditNote>().AsNoTracking().SingleAsync(p => p.CompanyId == companyId && p.DocumentNumber == large.DocumentNumber))
            .Should().Match<PendingCreditNote>(p => p.Status == PendingCreditNoteStatus.Pending && p.NetAmount == 120m && p.SubmittedByUserId == valuer.Id);
        (await db.Set<PropertyValuation>().AsNoTracking().SingleAsync(v => v.PropertyId == demolished.Id && v.Kind == ValuationKind.Supplementary)).RecordedByUserId.Should().Be(valuer.Id);

        // Approved through the workflow, the credit note posts.
        IWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
        long instance = await db.WorkflowInstances.AsNoTracking().Where(i => i.CompanyId == companyId && i.DocumentType == WorkflowDocumentTypes.CreditNote && i.DocumentReference == large.DocumentNumber)
            .Select(i => i.Id).SingleAsync();
        (await workflow.ActAsync(instance, treasurer.Id, WorkflowActionType.Approve, null)).IsSuccess.Should().BeTrue();
        (await db.OpenItems.AsNoTracking().SingleAsync(i => i.CompanyId == companyId && i.DocumentNumber == large.DocumentNumber)).ItemType.Should().Be(OpenItemType.CreditNote);

        // An objection upheld from the opening value credits 15 over three bills: under the threshold, it posts at once, and the decider is recorded.
        string objection = (await roll.LodgeObjectionAsync(companyId, small.Id, new DateOnly(2026, 3, 25), 45_000m, "Boundary is smaller than recorded")).Value;
        ValuationAdjustment minor = (await roll.DecideObjectionAsync(companyId, objection, new DateOnly(2026, 4, 2), 45_000m, "Survey confirms", treasurer.Id)).Value;
        (minor.BillsAdjusted, minor.Amount, minor.HeldForApproval).Should().Be((3, -15m, false));
        (await db.OpenItems.AnyAsync(i => i.CompanyId == companyId && i.DocumentNumber == minor.DocumentNumber)).Should().BeTrue();
        (await db.Set<ValuationObjection>().AsNoTracking().SingleAsync(o => o.CompanyId == companyId && o.ObjectionNumber == objection)).DecidedByUserId.Should().Be(treasurer.Id);
        (await db.Set<PropertyValuation>().AsNoTracking().SingleAsync(v => v.PropertyId == small.Id && v.Kind == ValuationKind.Objection)).RecordedByUserId.Should().Be(treasurer.Id);
    }
}
