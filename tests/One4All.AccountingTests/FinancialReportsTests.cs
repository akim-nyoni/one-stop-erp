using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class FinancialReportsTests
{
    private readonly AccountingFixture _fixture;

    public FinancialReportsTests(AccountingFixture fixture) => _fixture = fixture;

    private static JournalLineRequest L(string account, JournalSide side, decimal amount) =>
        new() { AccountCode = account, Side = side, TransactionAmount = amount };

    private async Task<long> SeedAndPostAsync(ServiceProvider provider)
    {
        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            companyId = await Scenario.SeedAsync(scope.ServiceProvider.GetRequiredService<One4AllDbContext>());
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();

            // Sales invoice (AR sub-ledger): Dr AR 115, Cr Sales 100, Cr VAT 15.
            (await engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "AR", PostingDate = new DateOnly(2026, 3, 10),
                TransactionCurrency = Scenario.Usd, DocumentType = "SalesInvoice",
                Lines = [L("1200", JournalSide.Debit, 115m), L("4000", JournalSide.Credit, 100m), L("2200", JournalSide.Credit, 15m)],
            })).IsSuccess.Should().BeTrue();

            // Receipt (Cash Book): Dr Bank 115, Cr AR 115.
            (await engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "CB", PostingDate = new DateOnly(2026, 3, 20),
                TransactionCurrency = Scenario.Usd, DocumentType = "Receipt",
                Lines = [L("1100", JournalSide.Debit, 115m), L("1200", JournalSide.Credit, 115m)],
            })).IsSuccess.Should().BeTrue();

            // Cost of sales (manual GL): Dr COGS 60, Cr Bank 60.
            (await engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 25),
                TransactionCurrency = Scenario.Usd, EnforceManualJournalRules = true, Narrative = "COGS",
                Lines = [L("5000", JournalSide.Debit, 60m), L("1100", JournalSide.Credit, 60m)],
            })).IsSuccess.Should().BeTrue();
        }

        return companyId;
    }

    [SkippableFact]
    public async Task Trial_balance_balances()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        long companyId = await SeedAndPostAsync(provider);

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IGeneralLedgerReports reports = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
        TrialBalanceReport tb = await reports.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));

        tb.IsBalanced.Should().BeTrue($"TB debits {tb.TotalDebit} must equal credits {tb.TotalCredit}");
        tb.TotalDebit.Should().Be(115m); // Bank 55 + COGS 60 (AR nets to 0 and is excluded)
        tb.Rows.Should().Contain(r => r.AccountCode == "4000" && r.Credit == 100m);
    }

    [SkippableFact]
    public async Task Income_statement_shows_net_profit()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        long companyId = await SeedAndPostAsync(provider);

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IGeneralLedgerReports reports = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
        IncomeStatement pl = await reports.IncomeStatementAsync(companyId, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        pl.TotalIncome.Should().Be(100m);
        pl.TotalExpenses.Should().Be(60m);
        pl.NetProfit.Should().Be(40m);
    }

    [SkippableFact]
    public async Task Balance_sheet_balances_with_current_year_earnings()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        long companyId = await SeedAndPostAsync(provider);

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IGeneralLedgerReports reports = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
        BalanceSheet bs = await reports.BalanceSheetAsync(companyId, new DateOnly(2026, 3, 31));

        bs.TotalAssets.Should().Be(55m);       // Bank 115 - 60
        bs.TotalLiabilities.Should().Be(15m);  // VAT output
        bs.TotalEquity.Should().Be(40m);       // current-year earnings
        bs.IsBalanced.Should().BeTrue();
    }

    [SkippableFact]
    public async Task Nominal_activity_runs_a_balance()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        long companyId = await SeedAndPostAsync(provider);

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IGeneralLedgerReports reports = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
        NominalActivityReport bank = await reports.NominalActivityAsync(companyId, "1100", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        bank.OpeningBalance.Should().Be(0m);
        bank.Lines.Should().HaveCount(2);
        bank.ClosingBalance.Should().Be(55m);
        bank.Lines[^1].RunningBalance.Should().Be(55m);
    }
}
