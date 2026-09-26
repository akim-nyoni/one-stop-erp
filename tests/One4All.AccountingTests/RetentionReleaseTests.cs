using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.JobCosting;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.JobCosting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RetentionReleaseTests
{
    private readonly AccountingFixture _fixture;

    public RetentionReleaseTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Releasing_retention_moves_it_onto_the_debtor()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long contractId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer client = new(companyId, "RET-C", "Developer", Scenario.Usd);
            db.Customers.Add(client);
            await db.SaveChangesAsync();

            IJobCostingService job = scope.ServiceProvider.GetRequiredService<IJobCostingService>();
            contractId = (await job.CreateContractAsync(companyId, "RET-1", "Clinic", client.Id, 1_000_000m, 800_000m, 10m, "4300")).Value;
            await job.CaptureCostAsync(companyId, contractId, "Steel", JobCostSource.PurchaseInvoice, 200_000m, new DateOnly(2026, 2, 1), "AP-R1");
            // Certify 250,000 gross → 25,000 retention held in 1250, net 225,000 on AR.
            (await job.CertifyProgressAsync(companyId, contractId, "IPC-R1", new DateOnly(2026, 3, 1), 200_000m, 50_000m, "1250")).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IJobCostingService job = scope.ServiceProvider.GetRequiredService<IJobCostingService>();

            (await job.ReleaseRetentionAsync(companyId, contractId, 0m, new DateOnly(2026, 9, 1), "1250", "RET-REL0")).IsFailure.Should().BeTrue();
            (await job.ReleaseRetentionAsync(companyId, contractId, 30_000m, new DateOnly(2026, 9, 1), "1250", "RET-RELX")).IsFailure.Should().BeTrue(); // more than held

            var release = await job.ReleaseRetentionAsync(companyId, contractId, 25_000m, new DateOnly(2026, 9, 1), "1250", "RET-REL1");
            release.IsSuccess.Should().BeTrue(release.IsFailure ? release.Error.ToString() : null);
            release.Value.Amount.Should().Be(25_000m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            JobContract contract = await db.Set<JobContract>().FirstAsync(c => c.Id == contractId);
            contract.RetentionHeld.Should().Be(0m);

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 9, 30));
            tb.IsBalanced.Should().BeTrue();
            // Retention receivable holding account nets to zero once released.
            tb.Rows.Should().NotContain(r => r.AccountCode == "1250" && (r.Debit != 0m || r.Credit != 0m));

            // The debtor now carries the net certified plus the released retention: 225,000 + 25,000 = 250,000.
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            AgeingReport debtors = await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 9, 30));
            debtors.Rows.Should().Contain(r => r.TraderCode == "RET-C" && r.Total == 250_000m);
        }
    }
}
