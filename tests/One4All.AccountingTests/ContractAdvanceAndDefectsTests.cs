using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.JobCosting;
using One4All.Core.Accounting;
using One4All.Core.JobCosting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers client certificates on a contract (brief §4.12): approved variation work is certified apart and never beyond what was approved; a client advance
/// is recovered from each certificate at its percentage, never past what is outstanding; and retention is released half at practical completion and the
/// rest when the defects-liability period ends.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ContractAdvanceAndDefectsTests
{
    private readonly AccountingFixture _fixture;

    public ContractAdvanceAndDefectsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Certificates_take_variations_recover_the_advance_and_retention_follows_the_defects_period()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.AddRange(new LedgerAccount(companyId, "4391", "Contract revenue", AccountType.Income), new LedgerAccount(companyId, "1291", "Retention receivable", AccountType.Asset),
            new LedgerAccount(companyId, "2391", "Client advances", AccountType.Liability));
        Customer client = new(companyId, "CA-C", "Road Authority", Scenario.Usd);
        db.Customers.Add(client);
        await db.SaveChangesAsync();

        IJobCostingService jobs = scope.ServiceProvider.GetRequiredService<IJobCostingService>();
        long contract = (await jobs.CreateContractAsync(companyId, "CA-K1", "Bypass", client.Id, 100_000m, 80_000m, 10m, "4391")).Value;
        (await jobs.ReceiveAdvanceAsync(companyId, contract, 10_000m, 20m, "2391", "1100", new DateOnly(2026, 2, 1), "CA-ADV")).IsSuccess.Should().BeTrue();

        IContractControlService control = scope.ServiceProvider.GetRequiredService<IContractControlService>();
        long vo = (await control.SubmitVariationAsync(companyId, contract, "VO-1", "Extra culvert", 8_000m, 6_000m, new DateOnly(2026, 2, 10))).Value;
        (await control.DecideVariationAsync(companyId, vo, true, new DateOnly(2026, 2, 12), null, null)).IsSuccess.Should().BeTrue();

        (await jobs.CertifyProgressAsync(companyId, contract, "CA-IPC-0", new DateOnly(2026, 3, 1), 20_000m, 0m, "1291", default, 9_000m)).Error.Code.Should().Be("Job.VariationsExceed");

        // 30,000 of work and 5,000 of variations: retention 3,500, advance recovered 20% = 7,000 → pays 24,500.
        CertificateResult first = (await jobs.CertifyProgressAsync(companyId, contract, "CA-IPC-1", new DateOnly(2026, 3, 1), 30_000m, 0m, "1291", default, 5_000m)).Value;
        (first.Gross, first.Retention, first.AdvanceRecovered, first.NetPayable).Should().Be((35_000m, 3_500m, 7_000m, 24_500m));

        // 25,000 more: 20% would be 5,000 but only 3,000 of the advance is left.
        CertificateResult second = (await jobs.CertifyProgressAsync(companyId, contract, "CA-IPC-2", new DateOnly(2026, 4, 1), 55_000m, 0m, "1291", default, 5_000m)).Value;
        (second.Gross, second.AdvanceRecovered, second.NetPayable).Should().Be((25_000m, 3_000m, 19_500m));
        decimal Balance(string account) => db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId).SelectMany(j => j.Lines).Where(l => l.AccountCode == account)
            .Sum(l => l.BaseCredit - l.BaseDebit);
        Balance("2391").Should().Be(0m, "the advance is fully recovered");
        Balance("4391").Should().Be(60_000m, "revenue is the gross certified");

        // Retention held 6,000: none before completion, half after it, the rest when the 3-month defects period ends.
        (await jobs.SetPracticalCompletionAsync(companyId, contract, new DateOnly(2026, 6, 30), 3)).IsSuccess.Should().BeTrue();
        (await jobs.ReleaseRetentionAsync(companyId, contract, 1_000m, new DateOnly(2026, 6, 1), "1291", "CA-RR-0")).Error.Code.Should().Be("Job.RetentionNotDue");
        (await jobs.ReleaseRetentionAsync(companyId, contract, 3_500m, new DateOnly(2026, 7, 1), "1291", "CA-RR-1")).Error.Code.Should().Be("Job.RetentionNotDue");
        (await jobs.ReleaseRetentionAsync(companyId, contract, 3_000m, new DateOnly(2026, 7, 1), "1291", "CA-RR-1")).IsSuccess.Should().BeTrue();
        (await jobs.ReleaseRetentionAsync(companyId, contract, 100m, new DateOnly(2026, 9, 29), "1291", "CA-RR-2")).Error.Code.Should().Be("Job.RetentionNotDue");
        (await jobs.ReleaseRetentionAsync(companyId, contract, 3_000m, new DateOnly(2026, 9, 30), "1291", "CA-RR-2")).IsSuccess.Should().BeTrue();
        (await db.Set<JobContract>().AsNoTracking().SingleAsync(c => c.Id == contract)).RetentionHeld.Should().Be(0m);
    }
}
