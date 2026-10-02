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
/// Covers percentage-of-completion revenue recognition: an under-billed contract books a contract asset, a
/// later run after more certification unwinds it and books a liability, and an unchanged position posts
/// nothing — with the ledger balancing throughout.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class RevenueRecognitionTests
{
    private const string ContractAsset = "1270";
    private const string ContractLiability = "2350";
    private const string Revenue = "4300";

    private readonly AccountingFixture _fixture;

    public RevenueRecognitionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Poc_runs_book_the_movement_between_earned_and_certified_revenue()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long contractId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            db.Accounts.Add(new LedgerAccount(companyId, ContractAsset, "Contract Asset (unbilled revenue)", AccountType.Asset));
            db.Accounts.Add(new LedgerAccount(companyId, ContractLiability, "Contract Liability (over-billing)", AccountType.Liability));
            Customer client = new(companyId, "POC-C", "Ministry of Works", Scenario.Usd);
            db.Customers.Add(client);
            await db.SaveChangesAsync();

            IJobCostingService job = scope.ServiceProvider.GetRequiredService<IJobCostingService>();
            contractId = (await job.CreateContractAsync(companyId, "POC-1", "School block", client.Id, 1_000_000m, 800_000m, 10m, Revenue)).Value;
            // 200,000 of 800,000 cost → 25% → earned 250,000. Certified 200,000 → under-billed by 50,000.
            (await job.CaptureCostAsync(companyId, contractId, "Steel", JobCostSource.PurchaseInvoice, 200_000m, new DateOnly(2026, 2, 1), "AP-P1")).IsSuccess.Should().BeTrue();
            (await job.CertifyProgressAsync(companyId, contractId, "IPC-P1", new DateOnly(2026, 2, 28), 200_000m, 0m, "1250")).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IJobCostingService job = scope.ServiceProvider.GetRequiredService<IJobCostingService>();
            (await job.RecogniseRevenueAsync(companyId, contractId, new DateOnly(2026, 2, 28), " ", ContractLiability, "POC-R0")).Error.Code.Should().Be("Job.PocAccounts");
            (await job.RecogniseRevenueAsync(companyId, 999_999, new DateOnly(2026, 2, 28), ContractAsset, ContractLiability, "POC-R0")).Error.Code.Should().Be("Job.ContractNotFound");

            var first = await job.RecogniseRevenueAsync(companyId, contractId, new DateOnly(2026, 2, 28), ContractAsset, ContractLiability, "POC-R1");
            first.IsSuccess.Should().BeTrue(first.IsFailure ? first.Error.ToString() : null);
            first.Value.PercentComplete.Should().Be(25m);
            first.Value.EarnedToDate.Should().Be(250_000m);
            first.Value.CumulativeAdjustment.Should().Be(50_000m);
            first.Value.Movement.Should().Be(50_000m);
            first.Value.JournalNumber.Should().NotBeNullOrEmpty();

            // Same position again → nothing posted.
            var again = await job.RecogniseRevenueAsync(companyId, contractId, new DateOnly(2026, 2, 28), ContractAsset, ContractLiability, "POC-R1b");
            again.Value.Movement.Should().Be(0m);
            again.Value.JournalNumber.Should().BeNull();
        }

        await Balances(provider, companyId, new DateOnly(2026, 2, 28), asset: 50_000m, liability: 0m, revenue: 250_000m);

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            // Certify a further 150,000 (350,000 to date) with no new cost: earned still 250,000 → over-billed by 100,000.
            IJobCostingService job = scope.ServiceProvider.GetRequiredService<IJobCostingService>();
            (await job.CertifyProgressAsync(companyId, contractId, "IPC-P2", new DateOnly(2026, 3, 31), 350_000m, 0m, "1250")).IsSuccess.Should().BeTrue();

            var second = await job.RecogniseRevenueAsync(companyId, contractId, new DateOnly(2026, 3, 31), ContractAsset, ContractLiability, "POC-R2");
            second.IsSuccess.Should().BeTrue(second.IsFailure ? second.Error.ToString() : null);
            second.Value.CumulativeAdjustment.Should().Be(-100_000m);
            second.Value.Movement.Should().Be(-150_000m); // unwind +50,000, book −100,000
        }

        // Asset cleared, liability 100,000, revenue = 350,000 certified − 100,000 = 250,000 earned.
        await Balances(provider, companyId, new DateOnly(2026, 3, 31), asset: 0m, liability: 100_000m, revenue: 250_000m);
    }

    private static async Task Balances(ServiceProvider provider, long companyId, DateOnly asAt, decimal asset, decimal liability, decimal revenue)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IConsolidationService tbService = scope.ServiceProvider.GetRequiredService<IConsolidationService>();
        ConsolidatedTrialBalance tb = (await tbService.ConsolidatedTrialBalanceAsync([companyId], asAt)).Value;
        tb.IsBalanced.Should().BeTrue();

        Net(tb, ContractAsset).Should().Be(asset);
        Net(tb, ContractLiability).Should().Be(-liability);
        Net(tb, Revenue).Should().Be(-revenue);
    }

    private static decimal Net(ConsolidatedTrialBalance tb, string code)
    {
        ConsolidatedTrialBalanceRow? row = tb.Rows.FirstOrDefault(r => r.AccountCode == code);
        return row is null ? 0m : row.Debit - row.Credit;
    }
}
