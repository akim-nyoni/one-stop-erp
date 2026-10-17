using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the common report options: a deferred journal shows in the trial balance only when unposted journals are included, and the trial balance and
/// statements translate into a reporting currency at the rate given.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class LedgerReportOptionsTests
{
    private readonly AccountingFixture _fixture;

    public LedgerReportOptionsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Unposted_journals_and_a_reporting_currency_are_options()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        JournalRequest Journal(string number, decimal amount, bool deferred) => new()
        {
            CompanyId = companyId, DocumentType = "JNL", DocumentNumber = number, PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd, Narrative = number, Deferred = deferred,
            Lines =
            [
                new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Debit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = amount },
            ],
        };
        (await engine.PostAsync(Journal("LRO-1", 1000m, deferred: false))).IsSuccess.Should().BeTrue();
        (await engine.PostAsync(Journal("LRO-2", 250m, deferred: true))).IsSuccess.Should().BeTrue();

        IGeneralLedgerReports reports = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
        DateOnly asAt = new(2026, 3, 31);
        (await reports.TrialBalanceAsync(companyId, asAt)).Rows.Single(r => r.AccountCode == "4000").Credit.Should().Be(1000m);
        (await reports.TrialBalanceAsync(companyId, asAt, new LedgerReportOptions(IncludeUnposted: true))).Rows.Single(r => r.AccountCode == "4000").Credit.Should().Be(1250m);

        TrialBalanceReport inZar = await reports.TrialBalanceAsync(companyId, asAt, new LedgerReportOptions(false, "ZAR", 18.5m));
        (inZar.Currency, inZar.Rows.Single(r => r.AccountCode == "4000").Credit).Should().Be(("ZAR", 18500m));
        inZar.TotalDebit.Should().Be(inZar.TotalCredit);
        (await reports.IncomeStatementAsync(companyId, new DateOnly(2026, 1, 1), asAt, new LedgerReportOptions(true, "ZAR", 18.5m))).Currency.Should().Be("ZAR");
        (await reports.BalanceSheetAsync(companyId, asAt, new LedgerReportOptions(true))).Assets.Single(a => a.AccountCode == "1100").Amount.Should().Be(1250m);

        // The same options on every ledger report, and a branch narrows the lines counted.
        db.Branches.Add(new One4All.Core.System.Branch(companyId, "EAST", "East branch"));
        await db.SaveChangesAsync();
        (await engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, DocumentType = "JNL", DocumentNumber = "LRO-3", PostingDate = new DateOnly(2026, 3, 12), TransactionCurrency = Scenario.Usd, Narrative = "East branch sale",
            Lines =
            [
                new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Debit, TransactionAmount = 70m, Branch = "EAST" },
                new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = 70m, Branch = "EAST" },
            ],
        })).IsSuccess.Should().BeTrue();
        DateOnly from = new(2026, 1, 1);
        (await reports.TrialBalanceAsync(companyId, asAt, new LedgerReportOptions(Branch: "east"))).Rows.Single(r => r.AccountCode == "4000").Credit.Should().Be(70m);
        (await reports.TrialBalanceByTypeAsync(companyId, asAt, new LedgerReportOptions(IncludeUnposted: true))).Rows.Single(r => r.Type == One4All.Core.Accounting.AccountType.Income).Credit.Should().Be(1320m);
        (await reports.TrialBalanceMovementAsync(companyId, from, asAt, new LedgerReportOptions(Branch: "EAST"))).Rows.Single(r => r.AccountCode == "4000").Credit.Should().Be(70m);
        CashFlowStatement east = await reports.CashFlowStatementAsync(companyId, from, asAt, new LedgerReportOptions(IncludeUnposted: true, Branch: "EAST"));
        east.Lines.Should().ContainSingle(l => l.Amount == 70m, "only the branch's sale counts towards the period's profit");
        (await reports.FinancialSnapshotAsync(companyId, asAt, new LedgerReportOptions(true, "ZAR", 2m))).Rows.Single(r => r.Metric == "Income (YTD)").Amount.Should().Be(2640m);
        NominalActivityReport sales = await reports.NominalActivityAsync(companyId, "4000", from, asAt, new LedgerReportOptions(IncludeUnposted: true));
        sales.Lines.Select(l => l.JournalNumber).Should().HaveCount(3);
        (await reports.NominalActivityAsync(companyId, "4000", from, asAt, new LedgerReportOptions(Branch: "EAST"))).Lines.Should().ContainSingle().Which.Credit.Should().Be(70m);
        JournalDaybookReport daybook = await reports.JournalDaybookAsync(companyId, from, asAt, new LedgerReportOptions(IncludeUnposted: true));
        daybook.Lines.Select(l => l.DocumentNumber).Should().Contain("LRO-2");
        (await reports.JournalDaybookAsync(companyId, from, asAt)).Lines.Select(l => l.DocumentNumber).Should().NotContain("LRO-2");
        (await reports.JournalDaybookAsync(companyId, from, asAt, new LedgerReportOptions(Branch: "EAST"))).Lines.Select(l => l.DocumentNumber).Should().Equal("LRO-3");
    }
}
