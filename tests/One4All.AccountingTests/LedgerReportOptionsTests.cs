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
    }
}
