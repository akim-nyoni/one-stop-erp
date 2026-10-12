using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Budgeting;
using One4All.Core.Accounting;
using One4All.Core.Budgeting;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers proposing unspent capital and approving its carry-forward into next year's capital budget as its own act.</summary>
[Collection(AccountingCollection.Name)]
public sealed class CapitalCarryForwardTests
{
    private readonly AccountingFixture _fixture;

    public CapitalCarryForwardTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Unspent_capital_is_carried_by_approval_only()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        long year2026 = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).SingleAsync();
        FiscalYear next = new(companyId, "2027", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31));
        db.FiscalYears.Add(next);
        await db.SaveChangesAsync();

        Budget capital = new(companyId, year2026, "CAP-2026", BudgetType.Capital, null, 1);
        capital.AddLine("1600", 1, 1000m);
        capital.Approve();
        Budget nextCapital = new(companyId, next.Id, "CAP-2027", BudgetType.Capital, null, 1);
        nextCapital.AddLine("1600", 1, 200m);
        nextCapital.Approve();
        Budget revenue = new(companyId, next.Id, "OPS-2027", BudgetType.OriginalApproved, null, 1);
        db.AddRange(capital, nextCapital, revenue);
        await db.SaveChangesAsync();

        // 600 spent on the vote in 2026.
        (await scope.ServiceProvider.GetRequiredService<IPostingEngine>().PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 6, 1), TransactionCurrency = Scenario.Usd, DocumentType = "Journal", DocumentNumber = "CCF-1",
            Narrative = "Capital works", Lines =
            [
                new JournalLineRequest { AccountCode = "1600", Side = JournalSide.Debit, TransactionAmount = 600m },
                new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = 600m },
            ],
        })).IsSuccess.Should().BeTrue();

        ICapitalCarryForwardService carry = scope.ServiceProvider.GetRequiredService<ICapitalCarryForwardService>();
        DateOnly yearEnd = new(2026, 12, 31);
        CapitalUnspent vote = (await carry.ProposeAsync(companyId, year2026, yearEnd)).Value.Should().ContainSingle().Subject;
        (vote.Budget, vote.Spent, vote.Carryable).Should().Be((1000m, 600m, 400m));

        (await carry.ApproveAsync(companyId, year2026, revenue.Id, [new CarryForwardAmount("1600", 100m)], "RES-9", 7, yearEnd)).Error.Code.Should().Be("Bud.CarryTarget");
        (await carry.ApproveAsync(companyId, year2026, nextCapital.Id, [new CarryForwardAmount("1600", 500m)], "RES-9", 7, yearEnd)).Error.Code.Should().Be("Bud.CarryTooMuch");
        (await carry.ApproveAsync(companyId, year2026, nextCapital.Id, [new CarryForwardAmount("1600", 300m)], "RES-9", 7, yearEnd)).Value.Should().Be(300m);
        (await carry.ProposeAsync(companyId, year2026, yearEnd)).Value.Single().Carryable.Should().Be(100m);

        Budget carried = await db.Set<Budget>().AsNoTracking().Include(b => b.Lines).SingleAsync(b => b.Id == nextCapital.Id);
        carried.Lines.Sum(l => l.Amount).Should().Be(500m);
        carried.VerifySeal().Should().BeTrue();
        (await db.Set<CapitalCarryForward>().AsNoTracking().SingleAsync(c => c.ToBudgetId == nextCapital.Id)).Reference.Should().Be("RES-9");
    }
}
