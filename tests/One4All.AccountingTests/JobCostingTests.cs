using FluentAssertions;
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
public sealed class JobCostingTests
{
    private readonly AccountingFixture _fixture;

    public JobCostingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Progress_certificate_raises_net_ar_and_holds_retention()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer client = new(companyId, "CLIENT-1", "Public Works Dept", Scenario.Usd);
            db.Customers.Add(client);
            await db.SaveChangesAsync();
            customerId = client.Id;
        }

        long contractId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IJobCostingService job = scope.ServiceProvider.GetRequiredService<IJobCostingService>();
            var created = await job.CreateContractAsync(companyId, "C-100", "Clinic block", customerId, 1_000_000m, 800_000m, retentionPercent: 10m, "4300");
            created.IsSuccess.Should().BeTrue(created.IsFailure ? created.Error.ToString() : null);
            contractId = created.Value;

            await job.CaptureCostAsync(companyId, contractId, "Concrete", JobCostSource.PurchaseInvoice, 120_000m, new DateOnly(2026, 2, 10), "AP-1");
            await job.CaptureCostAsync(companyId, contractId, "Labour", JobCostSource.Labour, 80_000m, new DateOnly(2026, 2, 28), "TS-1");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IJobCostingService job = scope.ServiceProvider.GetRequiredService<IJobCostingService>();
            // Work done 200,000 + materials 50,000 = 250,000 gross; 10% retention → net 225,000.
            var cert = await job.CertifyProgressAsync(companyId, contractId, "IPC-1", new DateOnly(2026, 3, 1), 200_000m, 50_000m, "1250");
            cert.IsSuccess.Should().BeTrue(cert.IsFailure ? cert.Error.ToString() : null);
            cert.Value.Gross.Should().Be(250_000m);
            cert.Value.Retention.Should().Be(25_000m);
            cert.Value.NetPayable.Should().Be(225_000m);

            var profit = await job.GetProfitabilityAsync(companyId, contractId);
            profit.Value.ActualCost.Should().Be(200_000m);
            profit.Value.PercentComplete.Should().Be(25m); // 200k / 800k budget
            profit.Value.CertifiedToDate.Should().Be(250_000m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            AgeingReport debtors = await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 3, 31));
            debtors.Total.Should().Be(225_000m); // net only; retention sits in the retention receivable

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "1250" && r.Debit == 25_000m);
            tb.Rows.Should().Contain(r => r.AccountCode == "4300" && r.Credit == 250_000m);
        }
    }
}
