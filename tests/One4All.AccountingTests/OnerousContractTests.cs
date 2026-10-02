using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.JobCosting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.JobCosting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers budget revision and the onerous-contract provision: a profitable contract provides nothing, a
/// revised forecast above the contract value provides the whole loss at once, a further revision posts only
/// the movement, and an improved forecast releases the provision — with the ledger balancing throughout.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class OnerousContractTests
{
    private const string LossExpense = "5900";
    private const string Provision = "2360";

    private readonly AccountingFixture _fixture;

    public OnerousContractTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Budget_revisions_drive_the_expected_loss_provision_up_and_down()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long contractId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            db.Accounts.Add(new LedgerAccount(companyId, LossExpense, "Expected Contract Losses", AccountType.Expense));
            db.Accounts.Add(new LedgerAccount(companyId, Provision, "Provision for Onerous Contracts", AccountType.Liability));
            Customer client = new(companyId, "ON-C", "City Council", Scenario.Usd);
            db.Customers.Add(client);
            await db.SaveChangesAsync();

            IJobCostingService job = scope.ServiceProvider.GetRequiredService<IJobCostingService>();
            contractId = (await job.CreateContractAsync(companyId, "ON-1", "Road upgrade", client.Id, 1_000_000m, 800_000m, 10m, "4300")).Value;
            (await job.CaptureCostAsync(companyId, contractId, "Earthworks", JobCostSource.Subcontractor, 300_000m, new DateOnly(2026, 2, 1), "SC-1")).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IJobCostingService job = scope.ServiceProvider.GetRequiredService<IJobCostingService>();
            DateOnly date = new(2026, 2, 28);

            // Profitable: nothing to provide.
            var none = await job.ProvideForExpectedLossAsync(companyId, contractId, date, LossExpense, Provision, "ON-P0");
            none.IsSuccess.Should().BeTrue(none.IsFailure ? none.Error.ToString() : null);
            none.Value.ExpectedLoss.Should().Be(0m);
            none.Value.JournalNumber.Should().BeNull();

            // Budget revision rules.
            (await job.ReviseBudgetAsync(companyId, contractId, 1_120_000m, " ")).Error.Code.Should().Be("Job.ReasonRequired");
            (await job.ReviseBudgetAsync(companyId, contractId, -1m, "typo")).Error.Code.Should().Be("Job.BudgetNegative");
            (await job.ReviseBudgetAsync(companyId, contractId, 200_000m, "below spend")).Error.Code.Should().Be("Job.BudgetBelowActual");
            (await job.ReviseBudgetAsync(companyId, 999_999, 1_120_000m, "x")).Error.Code.Should().Be("Job.ContractNotFound");

            // Forecast rises to 1,120,000 → onerous by 120,000: the whole loss is provided now.
            (await job.ReviseBudgetAsync(companyId, contractId, 1_120_000m, "Rock encountered; blasting required")).IsSuccess.Should().BeTrue();
            (await job.GetProfitabilityAsync(companyId, contractId)).Value.BudgetCost.Should().Be(1_120_000m);

            var provided = await job.ProvideForExpectedLossAsync(companyId, contractId, date, LossExpense, Provision, "ON-P1");
            provided.IsSuccess.Should().BeTrue(provided.IsFailure ? provided.Error.ToString() : null);
            provided.Value.Should().BeEquivalentTo(new { ExpectedLoss = 120_000m, PriorProvision = 0m, Movement = 120_000m });
            provided.Value.JournalNumber.Should().NotBeNullOrEmpty();

            // Re-run: nothing moves.
            (await job.ProvideForExpectedLossAsync(companyId, contractId, date, LossExpense, Provision, "ON-P1b")).Value.Movement.Should().Be(0m);
        }

        await Balances(provider, companyId, new DateOnly(2026, 2, 28), expense: 120_000m, provision: 120_000m);

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IJobCostingService job = scope.ServiceProvider.GetRequiredService<IJobCostingService>();
            DateOnly date = new(2026, 3, 31);

            // Forecast improves to 1,050,000 → loss 50,000: release 70,000.
            (await job.ReviseBudgetAsync(companyId, contractId, 1_050_000m, "Blasting cheaper than feared")).IsSuccess.Should().BeTrue();
            var released = await job.ProvideForExpectedLossAsync(companyId, contractId, date, LossExpense, Provision, "ON-P2");
            released.Value.Should().BeEquivalentTo(new { ExpectedLoss = 50_000m, PriorProvision = 120_000m, Movement = -70_000m });

            // Back to profitable → the remaining 50,000 is released in full.
            (await job.ReviseBudgetAsync(companyId, contractId, 950_000m, "Scope reduced by client")).IsSuccess.Should().BeTrue();
            var cleared = await job.ProvideForExpectedLossAsync(companyId, contractId, date, LossExpense, Provision, "ON-P3");
            cleared.Value.Should().BeEquivalentTo(new { ExpectedLoss = 0m, PriorProvision = 50_000m, Movement = -50_000m });
        }

        await Balances(provider, companyId, new DateOnly(2026, 3, 31), expense: 0m, provision: 0m);
    }

    private static async Task Balances(ServiceProvider provider, long companyId, DateOnly asAt, decimal expense, decimal provision)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IConsolidationService tbService = scope.ServiceProvider.GetRequiredService<IConsolidationService>();
        ConsolidatedTrialBalance tb = (await tbService.ConsolidatedTrialBalanceAsync([companyId], asAt)).Value;
        tb.IsBalanced.Should().BeTrue();
        Net(tb, LossExpense).Should().Be(expense);
        Net(tb, Provision).Should().Be(-provision);
    }

    private static decimal Net(ConsolidatedTrialBalance tb, string code)
    {
        ConsolidatedTrialBalanceRow? row = tb.Rows.FirstOrDefault(r => r.AccountCode == code);
        return row is null ? 0m : row.Debit - row.Credit;
    }
}
