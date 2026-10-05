using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.JobCosting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.JobCosting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers subcontracts and plant (brief §14 K 192, 194): certificates invoice the supplier ledger net of retention and hold the
/// retention, refuse backwards or over-value work, and release no more than is held; plant hours charge jobs at the hire rate;
/// JC192 and JC194 read them back.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SubcontractTests
{
    private readonly AccountingFixture _fixture;

    public SubcontractTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Subcontracts_post_through_payables_with_retention_and_plant_is_recovered()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        Customer client = new(companyId, "SCCLI", "Client", Scenario.Usd);
        Supplier subbie = new(companyId, "SUBBY", "Subbie Ltd", Scenario.Usd);
        PlantItem excavator = new(companyId, "exc1", "Excavator", 50m);
        db.AddRange(client, subbie, excavator, new LedgerAccount(companyId, "2476", "Retention Payable", AccountType.Liability));
        await db.SaveChangesAsync();
        long contract = (await scope.ServiceProvider.GetRequiredService<IJobCostingService>().CreateContractAsync(companyId, "CC-2", "Bridge", client.Id, 50_000m, 40_000m, 10m, "4300")).Value;

        ISubcontractService subcontracts = scope.ServiceProvider.GetRequiredService<ISubcontractService>();
        long sc = (await subcontracts.LetAsync(companyId, contract, subbie.Id, "sc-1", "Steelwork", 10_000m, 10m)).Value;
        SubcontractCertificateRequest Cert(string number, DateOnly date, decimal toDate) => new()
        {
            CompanyId = companyId, SubcontractId = sc, Number = number, Date = date, WorkDoneToDate = toDate, CostAccountCode = "5000", RetentionPayableAccountCode = "2476",
        };
        (await subcontracts.CertifyAsync(Cert("C1", new DateOnly(2026, 3, 10), 4_000m))).Value.Should().Be(new SubcontractCertificateResult(4_000m, 400m, 3_600m));
        (await subcontracts.CertifyAsync(Cert("C2", new DateOnly(2026, 3, 31), 3_000m))).Error.Code.Should().Be("Job.CertificateNoProgress");
        (await subcontracts.CertifyAsync(Cert("C2", new DateOnly(2026, 3, 31), 11_000m))).Error.Code.Should().Be("Job.CertificateOverValue");
        (await subcontracts.CertifyAsync(Cert("C2", new DateOnly(2026, 3, 31), 7_000m))).Value.Payable.Should().Be(2_700m);
        (await subcontracts.ReleaseRetentionAsync(companyId, sc, 800m, new DateOnly(2026, 3, 31), "2476", "SC-1/REL")).Error.Code.Should().Be("Job.RetentionRelease");
        (await subcontracts.ReleaseRetentionAsync(companyId, sc, 700m, new DateOnly(2026, 3, 31), "2476", "SC-1/REL")).IsSuccess.Should().BeTrue();

        decimal owed = await db.OpenItems.AsNoTracking().Where(o => o.CompanyId == companyId && o.TraderId == subbie.Id && o.Ledger == TradingLedger.AccountsPayable).SumAsync(o => o.OriginalBase);
        owed.Should().Be(7_000m);
        decimal retentionBalance = await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId).SelectMany(j => j.Lines).Where(l => l.AccountCode == "2476").SumAsync(l => l.BaseDebit - l.BaseCredit);
        retentionBalance.Should().Be(0m);

        (await subcontracts.LogPlantHoursAsync(companyId, excavator.Id, contract, new DateOnly(2026, 3, 2), 8m)).IsSuccess.Should().BeTrue();
        (await subcontracts.LogPlantHoursAsync(companyId, excavator.Id, contract, new DateOnly(2026, 3, 3), 10m)).IsSuccess.Should().BeTrue();
        (await subcontracts.LogPlantHoursAsync(companyId, excavator.Id, contract, new DateOnly(2026, 3, 2), 20m)).Error.Code.Should().Be("Job.PlantOverbooked");
        (await subcontracts.LogPlantCostAsync(companyId, excavator.Id, new DateOnly(2026, 3, 4), 300m, "Diesel")).IsSuccess.Should().BeTrue();
        (await db.JobContracts.AsNoTracking().Include(c => c.Costs).SingleAsync(c => c.Id == contract)).ActualCost.Should().Be(7_900m);

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            var rendered = await runner.RenderAsync(companyId, code, new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 3, 1), ["ToDate"] = new DateOnly(2026, 3, 31) }, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        string ledger = await Render("JC192");
        ledger.Should().Contain("CC-2,Subbie Ltd,SC-1,C1,2026-03-10,\"4,000.00\",400.00,\"7,000.00 / 10,000.00 · held 0.00\",3600")
            .And.Contain("CC-2,Subbie Ltd,SC-1,C2,2026-03-31,\"3,000.00\",300.00,").And.Contain("SC-1,Release,—,—,-700.00,");
        (await Render("JC194")).Should().Contain("EXC1,Excavator,18,176,10.2%,900.00,300.00,16.67,600");
    }
}
