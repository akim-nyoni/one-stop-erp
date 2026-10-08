using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the departmental income statement: the profit-and-loss restricted to journal lines tagged with a
/// department dimension.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class DepartmentalIncomeStatementTests
{
    private readonly AccountingFixture _fixture;

    public DepartmentalIncomeStatementTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Income_statement_can_be_filtered_to_one_department()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            await Scenario.DimensionsAsync(db, companyId, "NTH", "STH");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            DateOnly date = new(2026, 3, 10);

            // NTH: sales 1000, expense 300.  STH: sales 400.
            await engine.PostAsync(Journal(companyId, date, "N-SALE", ("1000", JournalSide.Debit, 1000m, null), ("4000", JournalSide.Credit, 1000m, "NTH")));
            await engine.PostAsync(Journal(companyId, date, "N-EXP", ("6800", JournalSide.Debit, 300m, "NTH"), ("1000", JournalSide.Credit, 300m, null)));
            await engine.PostAsync(Journal(companyId, date, "S-SALE", ("1000", JournalSide.Debit, 400m, null), ("4000", JournalSide.Credit, 400m, "STH")));
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            DateOnly from = new(2026, 3, 1);
            DateOnly to = new(2026, 3, 31);

            IncomeStatement north = await gl.IncomeStatementByDepartmentAsync(companyId, from, to, "NTH");
            north.TotalIncome.Should().Be(1000m);
            north.TotalExpenses.Should().Be(300m);
            north.NetProfit.Should().Be(700m);

            IncomeStatement south = await gl.IncomeStatementByDepartmentAsync(companyId, from, to, "STH");
            south.TotalIncome.Should().Be(400m);
            south.TotalExpenses.Should().Be(0m);
            south.NetProfit.Should().Be(400m);

            // The company-wide statement is the sum of both departments.
            IncomeStatement all = await gl.IncomeStatementAsync(companyId, from, to);
            all.TotalIncome.Should().Be(1400m);
            all.NetProfit.Should().Be(1100m);
        }
    }

    private static JournalRequest Journal(long companyId, DateOnly date, string doc, params (string Account, JournalSide Side, decimal Amount, string? Department)[] lines) => new()
    {
        CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = Scenario.Usd,
        EnforceManualJournalRules = false, DocumentType = "Journal", DocumentNumber = doc,
        Lines = lines.Select(l => new JournalLineRequest { AccountCode = l.Account, Side = l.Side, TransactionAmount = l.Amount, Department = l.Department }).ToList(),
    };
}
