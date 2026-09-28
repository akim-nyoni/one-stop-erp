using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the petty-cash imprest system: establish a float, disburse an expense, then reimburse the float
/// back to its set amount (only the shortfall is drawn from the bank).
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PettyCashTests
{
    private const string PettyCash = "1010"; // Cash in Safe
    private const string Bank = "1100";
    private const string Expense = "6300";   // Petty Cash Expense

    private readonly AccountingFixture _fixture;

    public PettyCashTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Imprest_float_is_established_spent_and_reimbursed()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPettyCashService petty = scope.ServiceProvider.GetRequiredService<IPettyCashService>();
            DateOnly date = new(2026, 3, 5);

            // Establish the 100 float from an empty balance.
            var establish = await petty.ReimburseToFloatAsync(companyId, date, PettyCash, Bank, 100m, "PC-INIT");
            establish.IsSuccess.Should().BeTrue(establish.IsFailure ? establish.Error.ToString() : null);
            establish.Value.ReimbursedAmount.Should().Be(100m);
            establish.Value.FloatBalanceAfter.Should().Be(100m);

            // Spend 30 on an expense.
            (await petty.DisburseAsync(companyId, 30m, new DateOnly(2026, 3, 10), Expense, PettyCash, "PC-1")).IsSuccess.Should().BeTrue();

            // Reimburse back to the 100 float — only the 30 shortfall is drawn.
            var top = await petty.ReimburseToFloatAsync(companyId, new DateOnly(2026, 3, 31), PettyCash, Bank, 100m, "PC-TOP");
            top.Value.ReimbursedAmount.Should().Be(30m);
            top.Value.FloatBalanceAfter.Should().Be(100m);

            // A second reimbursement when the float is full is a no-op.
            var again = await petty.ReimburseToFloatAsync(companyId, new DateOnly(2026, 3, 31), PettyCash, Bank, 100m, "PC-TOP2");
            again.Value.ReimbursedAmount.Should().Be(0m);
            again.Value.JournalNumber.Should().BeNull();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == PettyCash && r.Debit == 100m); // float restored
            tb.Rows.Should().Contain(r => r.AccountCode == Expense && r.Debit == 30m);    // expense recognised
        }
    }
}
