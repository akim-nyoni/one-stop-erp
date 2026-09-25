using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class AccrualPrepaymentTests
{
    private readonly AccountingFixture _fixture;

    public AccrualPrepaymentTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Accrual_hits_the_period_then_auto_reverses_next_period()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            IAccrualService accruals = scope.ServiceProvider.GetRequiredService<IAccrualService>();

            // Reversal not after posting date is refused.
            (await accruals.PostAccrualAsync(companyId, new DateOnly(2026, 3, 31), new DateOnly(2026, 3, 31), "6700", "2470", 500m, "ACC-1", "March travel accrual")).IsFailure.Should().BeTrue();
            // Non-positive amount refused.
            (await accruals.PostAccrualAsync(companyId, new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 1), "6700", "2470", 0m, "ACC-1", "bad")).IsFailure.Should().BeTrue();

            var result = await accruals.PostAccrualAsync(companyId, new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 1), "6700", "2470", 500m, "ACC-1", "March travel accrual");
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            result.Value.PostingJournalNumber.Should().NotBe(result.Value.ReversalJournalNumber);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();

            // At period end, the accrual sits: expense 6700 Dr 500, accrual liability 2470 Cr 500.
            TrialBalanceReport march = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            march.IsBalanced.Should().BeTrue();
            march.Rows.Should().Contain(r => r.AccountCode == "6700" && r.Debit == 500m);
            march.Rows.Should().Contain(r => r.AccountCode == "2470" && r.Credit == 500m);

            // Once the reversal lands, both net to zero.
            TrialBalanceReport april = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
            april.IsBalanced.Should().BeTrue();
            april.Rows.Should().NotContain(r => r.AccountCode == "6700" && (r.Debit != 0m || r.Credit != 0m));
            april.Rows.Should().NotContain(r => r.AccountCode == "2470" && (r.Debit != 0m || r.Credit != 0m));
        }
    }

    [SkippableFact]
    public async Task Prepayment_defers_the_cost_then_releases_it_next_period()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            IAccrualService accruals = scope.ServiceProvider.GetRequiredService<IAccrualService>();
            var result = await accruals.PostPrepaymentAsync(companyId, new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 1), "1360", "6700", 300m, "PRE-1", "Prepaid March insurance");
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();

            // At period end the cost is deferred: prepayment asset 1360 Dr 300, expense 6700 Cr 300.
            TrialBalanceReport march = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            march.IsBalanced.Should().BeTrue();
            march.Rows.Should().Contain(r => r.AccountCode == "1360" && r.Debit == 300m);
            march.Rows.Should().Contain(r => r.AccountCode == "6700" && r.Credit == 300m);

            // March P&L is relieved of the cost: the deferral credits 6700, so it shows as a -300 expense.
            IncomeStatement marchPl = await gl.IncomeStatementAsync(companyId, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));
            marchPl.Expenses.Should().Contain(l => l.AccountCode == "6700" && l.Amount == -300m);

            // Cumulatively the reversing pair nets to zero on both accounts by the next period.
            TrialBalanceReport april = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
            april.IsBalanced.Should().BeTrue();
            april.Rows.Should().NotContain(r => r.AccountCode == "1360" && (r.Debit != 0m || r.Credit != 0m));
            april.Rows.Should().NotContain(r => r.AccountCode == "6700" && (r.Debit != 0m || r.Credit != 0m));

            // But the cost is released into the April P&L when the reversal lands.
            IncomeStatement aprilPl = await gl.IncomeStatementAsync(companyId, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));
            aprilPl.Expenses.Should().Contain(l => l.AccountCode == "6700" && l.Amount == 300m);
        }
    }
}
