using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.JobCosting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers subcontract advances and withholding: the advance is carried as an asset and recovered as a share of each certificate until
/// cleared, withholding tax is deducted from each certificate's gross and certified for the return, and the supplier is invoiced the rest.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SubcontractAdvanceTests
{
    private readonly AccountingFixture _fixture;

    public SubcontractAdvanceTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Certificates_recover_the_advance_and_withhold_tax()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.AddRange(new LedgerAccount(companyId, "2495", "Withholding Tax Payable", AccountType.Liability), new LedgerAccount(companyId, "2476", "Retention Payable", AccountType.Liability),
            new LedgerAccount(companyId, "1497", "Advances to Subcontractors", AccountType.Asset));
        TaxCode wht = new(companyId, "WHT10", "Withholding 10%", TaxType.Withholding) { OutputAccountCode = "2495", IsActive = true };
        wht.AddRate(new DateOnly(2026, 1, 1), 10m);
        Customer client = new(companyId, "SADV-CLI", "Client", Scenario.Usd);
        Supplier subbie = new(companyId, "SADV-SUB", "Electricians Ltd", Scenario.Usd);
        db.AddRange(wht, client, subbie);
        await db.SaveChangesAsync();
        long contract = (await scope.ServiceProvider.GetRequiredService<IJobCostingService>().CreateContractAsync(companyId, "SADV-1", "Depot", client.Id, 50_000m, 40_000m, 5m, "4000")).Value;

        ISubcontractService subcontracts = scope.ServiceProvider.GetRequiredService<ISubcontractService>();
        long sc = (await subcontracts.LetAsync(companyId, contract, subbie.Id, "SADV-SC", "Wiring", 10_000m, 10m)).Value;
        (await subcontracts.SetWithholdingAsync(companyId, sc, "NOPE")).Error.Code.Should().Be("Ap.WithholdingCode");
        (await subcontracts.SetWithholdingAsync(companyId, sc, "wht10")).IsSuccess.Should().BeTrue();
        (await subcontracts.PayAdvanceAsync(companyId, sc, 12_000m, 25m, new DateOnly(2026, 4, 1), "1100", "1497", "ADV-1")).Error.Code.Should().Be("Job.AdvanceTooLarge");
        (await subcontracts.PayAdvanceAsync(companyId, sc, 2_000m, 25m, new DateOnly(2026, 4, 1), "1100", "1497", "ADV-1")).IsSuccess.Should().BeTrue();

        SubcontractCertificateRequest Cert(string number, decimal toDate) => new()
        {
            CompanyId = companyId, SubcontractId = sc, Number = number, Date = new DateOnly(2026, 4, 20), WorkDoneToDate = toDate, CostAccountCode = "5000", RetentionPayableAccountCode = "2476",
        };

        // 4,000 gross: 400 retention, a quarter (1,000) against the advance, 400 withheld.
        (await subcontracts.CertifyAsync(Cert("C1", 4_000m))).Value.Should().Be(new SubcontractCertificateResult(4_000m, 400m, 2_200m, 1_000m, 400m));

        // 6,000 gross: the quarter would be 1,500 but only 1,000 of the advance is left.
        (await subcontracts.CertifyAsync(Cert("C2", 10_000m))).Value.Should().Be(new SubcontractCertificateResult(6_000m, 600m, 3_800m, 1_000m, 600m));

        async Task<decimal> Balance(string account) => await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId).SelectMany(j => j.Lines).Where(l => l.AccountCode == account)
            .SumAsync(l => l.BaseDebit - l.BaseCredit);
        (await Balance("1497")).Should().Be(0m);
        (await Balance("2495")).Should().Be(-1_000m);
        (await Balance("5000")).Should().Be(10_000m);
        (await db.OpenItems.AsNoTracking().Where(o => o.CompanyId == companyId && o.TraderId == subbie.Id && o.Ledger == TradingLedger.AccountsPayable).SumAsync(o => o.OriginalBase)).Should().Be(6_000m);

        WithholdingReturn taxReturn = (await scope.ServiceProvider.GetRequiredService<IWithholdingTaxService>().ReturnAsync(companyId, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30))).Value;
        taxReturn.Lines.Select(l => l.Withheld).Should().BeEquivalentTo([400m, 600m]);
    }
}
