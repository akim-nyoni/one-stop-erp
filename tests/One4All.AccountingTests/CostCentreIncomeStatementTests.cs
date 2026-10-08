using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers the cost-centre income statement: the P&amp;L restricted to a cost-centre dimension.</summary>
[Collection(AccountingCollection.Name)]
public sealed class CostCentreIncomeStatementTests
{
    private readonly AccountingFixture _fixture;

    public CostCentreIncomeStatementTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Income_statement_can_be_filtered_to_one_cost_centre()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            await Scenario.DimensionsAsync(db, companyId, "CC1", "CC2");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            DateOnly date = new(2026, 3, 10);

            // CC1: sales 900, expense 200.  CC2: sales 300.
            await engine.PostAsync(Journal(companyId, date, "C1-SALE", ("1000", JournalSide.Debit, 900m, null), ("4000", JournalSide.Credit, 900m, "CC1")));
            await engine.PostAsync(Journal(companyId, date, "C1-EXP", ("6800", JournalSide.Debit, 200m, "CC1"), ("1000", JournalSide.Credit, 200m, null)));
            await engine.PostAsync(Journal(companyId, date, "C2-SALE", ("1000", JournalSide.Debit, 300m, null), ("4000", JournalSide.Credit, 300m, "CC2")));
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            DateOnly from = new(2026, 3, 1);
            DateOnly to = new(2026, 3, 31);

            IncomeStatement cc1 = await gl.IncomeStatementByCostCentreAsync(companyId, from, to, "CC1");
            cc1.TotalIncome.Should().Be(900m);
            cc1.TotalExpenses.Should().Be(200m);
            cc1.NetProfit.Should().Be(700m);

            IncomeStatement cc2 = await gl.IncomeStatementByCostCentreAsync(companyId, from, to, "CC2");
            cc2.TotalIncome.Should().Be(300m);
            cc2.NetProfit.Should().Be(300m);

            // Company-wide is the sum of both cost centres.
            IncomeStatement all = await gl.IncomeStatementAsync(companyId, from, to);
            all.TotalIncome.Should().Be(1200m);
            all.NetProfit.Should().Be(1000m);
        }
    }

    private static JournalRequest Journal(long companyId, DateOnly date, string doc, params (string Account, JournalSide Side, decimal Amount, string? CostCentre)[] lines) => new()
    {
        CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = Scenario.Usd,
        EnforceManualJournalRules = false, DocumentType = "Journal", DocumentNumber = doc,
        Lines = lines.Select(l => new JournalLineRequest { AccountCode = l.Account, Side = l.Side, TransactionAmount = l.Amount, CostCentre = l.CostCentre }).ToList(),
    };
}
