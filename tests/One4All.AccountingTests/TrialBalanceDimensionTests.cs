using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers a trial balance for one cost centre or department: only the lines analysed to it count.</summary>
[Collection(AccountingCollection.Name)]
public sealed class TrialBalanceDimensionTests
{
    private readonly AccountingFixture _fixture;

    public TrialBalanceDimensionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_trial_balance_can_be_drawn_for_one_cost_centre_or_department()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await Scenario.DimensionsAsync(db, companyId, "NTH", "STH");
        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        async Task Spend(string document, string centre, decimal amount) => (await engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd, EnforceManualJournalRules = false,
            DocumentType = "Journal", DocumentNumber = document, Narrative = "Office supplies",
            Lines =
            [
                new JournalLineRequest { AccountCode = "6800", Side = JournalSide.Debit, TransactionAmount = amount, CostCentre = centre, Department = centre },
                new JournalLineRequest { AccountCode = "1000", Side = JournalSide.Credit, TransactionAmount = amount, CostCentre = centre, Department = centre },
            ],
        })).IsSuccess.Should().BeTrue();
        await Spend("TBD-1", "NTH", 100m);
        await Spend("TBD-2", "STH", 40m);

        IGeneralLedgerReports reports = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
        TrialBalanceReport all = await reports.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31), LedgerReportOptions.Default);
        all.Rows.Single(r => r.AccountCode == "6800").Debit.Should().Be(140m);
        TrialBalanceReport north = await reports.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31), new LedgerReportOptions(CostCentre: "nth"));
        north.Rows.Single(r => r.AccountCode == "6800").Debit.Should().Be(100m);
        north.IsBalanced.Should().BeTrue();
        (await reports.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31), new LedgerReportOptions(Department: "STH"))).Rows.Single(r => r.AccountCode == "6800").Debit.Should().Be(40m);
    }
}
