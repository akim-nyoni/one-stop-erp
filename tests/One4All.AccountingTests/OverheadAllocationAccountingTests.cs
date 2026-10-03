using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers overhead recharges at period end: March rent of 1,000 in admin is recharged by floor area (IT 200, production
/// 600, sales 200), then IT's 300 plus the 200 it received is recharged by usage (production 333.33, sales 166.67). A
/// rerun posts nothing; a late 100 of rent is recharged on the next run and steps down through IT with new usage drivers.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class OverheadAllocationAccountingTests
{
    private const string Overheads = "6800";

    private readonly AccountingFixture _fixture;

    public OverheadAllocationAccountingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Overheads_are_recharged_step_down_and_only_once()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        foreach ((string code, string name) in new[] { ("ADM", "Administration"), ("ITS", "IT Services"), ("PRD", "Production"), ("SAL", "Sales") })
        {
            db.CostCentres.Add(new CostCentre(companyId, code, name));
        }

        await db.SaveChangesAsync();

        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        await Cost(engine, companyId, "ADM", 1_000m, "RENT-03");
        await Cost(engine, companyId, "ITS", 300m, "IT-03");

        IOverheadAllocationService allocation = scope.ServiceProvider.GetRequiredService<IOverheadAllocationService>();
        (await allocation.CreateRuleAsync(Rule(companyId, "BAD", AllocationBasis.FixedPercent, "ADM", ("PRD", 60m), ("SAL", 30m)))).Error.Code.Should().Be("Alloc.Drivers");
        (await allocation.CreateRuleAsync(Rule(companyId, "BAD", AllocationBasis.Headcount, "ADM", ("ADM", 1m), ("SAL", 3m)))).Error.Code.Should().Be("Alloc.SelfCharge");
        (await allocation.CreateRuleAsync(Rule(companyId, "BAD", AllocationBasis.Headcount, "ADM", ("XYZ", 1m)))).Error.Code.Should().Be("Alloc.CostCentre");

        (await allocation.CreateRuleAsync(Rule(companyId, "RENT", AllocationBasis.FloorArea, "ADM", ("ITS", 100m), ("PRD", 300m), ("SAL", 100m)) with { Sequence = 10 })).IsSuccess.Should().BeTrue();
        long itRule = (await allocation.CreateRuleAsync(Rule(companyId, "IT", AllocationBasis.UsageDriver, "ITS", ("PRD", 6m), ("SAL", 3m)) with { Sequence = 20 })).Value;

        DateOnly march = new(2026, 3, 15);
        AllocationRunResult preview = (await allocation.PreviewAsync(companyId, march)).Value;
        preview.Rules.Select(r => (r.RuleCode, r.Pool, r.JournalNumber)).Should().Equal(("RENT", 1_000m, (string?)null), ("IT", 500m, (string?)null));

        AllocationRunResult run = (await allocation.RunAsync(companyId, march)).Value;
        run.PeriodEnd.Should().Be(new DateOnly(2026, 3, 31));
        run.Rules.Should().OnlyContain(r => r.JournalNumber != null);
        run.Rules[1].Shares.Select(s => (s.CostCentre, s.Amount)).Should().Equal(("PRD", 333.33m), ("SAL", 166.67m));
        (await ByCostCentre(db, companyId)).Should().BeEquivalentTo(new Dictionary<string, decimal> { ["ADM"] = 0m, ["ITS"] = 0m, ["PRD"] = 933.33m, ["SAL"] = 366.67m });

        (await allocation.RunAsync(companyId, march)).Value.Rules.Should().OnlyContain(r => r.Pool == 0m && r.JournalNumber == null);

        // A late rent charge, with IT's usage now even: 100 → IT 20, production 60, sales 20; IT's 20 → 10 / 10.
        await Cost(engine, companyId, "ADM", 100m, "RENT-03B");
        (await allocation.SetDriversAsync(companyId, itRule, [new AllocationTargetRequest("PRD", null, 1m), new AllocationTargetRequest("SAL", null, 1m)])).IsSuccess.Should().BeTrue();
        (await allocation.RunAsync(companyId, march)).Value.Rules.Select(r => r.Pool).Should().Equal(100m, 20m);
        (await ByCostCentre(db, companyId)).Should().BeEquivalentTo(new Dictionary<string, decimal> { ["ADM"] = 0m, ["ITS"] = 0m, ["PRD"] = 1_003.33m, ["SAL"] = 396.67m });

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 3, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Single(r => r.AccountCode == Overheads).Debit.Should().Be(1_400m);
    }

    private static AllocationRuleRequest Rule(long companyId, string code, AllocationBasis basis, string source, params (string CostCentre, decimal Driver)[] targets) => new()
    {
        CompanyId = companyId, Code = code, Name = code + " recharge", Basis = basis, SourceAccountCode = Overheads, SourceCostCentre = source,
        Targets = [.. targets.Select(t => new AllocationTargetRequest(t.CostCentre, null, t.Driver))],
    };

    private static async Task Cost(IPostingEngine engine, long companyId, string costCentre, decimal amount, string document)
        => (await engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd, DocumentNumber = document,
            Lines =
            [
                new JournalLineRequest { AccountCode = Overheads, CostCentre = costCentre, Side = JournalSide.Debit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = amount },
            ],
        })).IsSuccess.Should().BeTrue();

    private static async Task<Dictionary<string, decimal>> ByCostCentre(One4AllDbContext db, long companyId)
        => await db.Set<JournalLine>().AsNoTracking()
            .Where(l => l.AccountCode == Overheads && db.Journals.Any(j => j.Id == l.JournalEntryId && j.CompanyId == companyId && j.Status == JournalStatus.Posted))
            .GroupBy(l => l.CostCentre)
            .ToDictionaryAsync(g => g.Key, g => g.Sum(l => l.BaseDebit - l.BaseCredit));
}
