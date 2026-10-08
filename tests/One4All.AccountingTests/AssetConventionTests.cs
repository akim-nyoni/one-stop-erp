using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.FixedAssets;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.FixedAssets;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers assets capitalised from supplier invoices (brief §4.14): the invoice's amount on the class's cost account becomes the asset without reposting, an
/// invoice coded to a clearing account is moved to cost, and no more than the invoice is ever capitalised; and the depreciation conventions.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AssetConventionTests
{
    private readonly AccountingFixture _fixture;

    public AssetConventionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Supplier_invoices_are_capitalised_once_and_conventions_set_the_first_charge()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.AddRange(new LedgerAccount(companyId, "1491", "Equipment at cost", AccountType.Asset), new LedgerAccount(companyId, "1492", "Equipment depreciation", AccountType.Asset),
            new LedgerAccount(companyId, "1493", "Asset clearing", AccountType.Asset), new LedgerAccount(companyId, "6191", "Depreciation", AccountType.Expense));
        Supplier supplier = new(companyId, "AC-SUP", "Machine Co", Scenario.Usd);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        IFixedAssetService assets = scope.ServiceProvider.GetRequiredService<IFixedAssetService>();
        (await assets.SaveClassAsync(new AssetClassRequest(companyId, "EQP", "Equipment", DepreciationMethod.StraightLine, 10, 0m, 0m, "1491", "1492", "6191", null,
            DepreciationConvention.DailyProRata))).IsSuccess.Should().BeTrue();
        IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
        (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = "AC-INV-1", DocumentDate = new DateOnly(2026, 4, 21), Currency = Scenario.Usd, NetAmount = 3_000m, ExpenseAccountCode = "1491",
        })).IsSuccess.Should().BeTrue();
        (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = "AC-INV-2", DocumentDate = new DateOnly(2026, 4, 10), Currency = Scenario.Usd, NetAmount = 1_000m, ExpenseAccountCode = "1493",
        })).IsSuccess.Should().BeTrue();

        CapitaliseInvoiceRequest From(string invoice, string asset, decimal? cost, string? account = null) => new()
        {
            CompanyId = companyId, InvoiceNumber = invoice, AssetNumber = asset, Description = "Lathe", AssetClass = "EQP", Cost = cost, InvoiceAccountCode = account,
        };

        // 3,000 coded straight to equipment: two assets share it, and nothing more can be taken.
        long lathe = (await assets.CapitaliseFromInvoiceAsync(From("AC-INV-1", "AC-1", 2_000m))).Value;
        (await assets.CapitaliseFromInvoiceAsync(From("AC-INV-1", "AC-2", null))).IsSuccess.Should().BeTrue();
        (await assets.CapitaliseFromInvoiceAsync(From("AC-INV-1", "AC-3", 1m))).Error.Code.Should().Be("Fa.InvoiceCapitalised");
        (await assets.CapitaliseFromInvoiceAsync(From("AC-NONE", "AC-4", null))).Error.Code.Should().Be("Fa.InvoiceNotFound");
        decimal Balance(string account) => db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId).SelectMany(j => j.Lines).Where(l => l.AccountCode == account)
            .Sum(l => l.BaseDebit - l.BaseCredit);
        Balance("1491").Should().Be(3_000m, "the invoice already put the cost on the asset account");

        // 1,000 coded to the clearing account is moved to cost.
        (await assets.CapitaliseFromInvoiceAsync(From("AC-INV-2", "AC-5", null, "1493"))).IsSuccess.Should().BeTrue();
        Balance("1493").Should().Be(0m);
        Balance("1491").Should().Be(4_000m);

        FixedAsset first = await db.FixedAssets.AsNoTracking().SingleAsync(a => a.Id == lathe);
        (first.Convention, first.AcquisitionDate, first.SourceAccountCode).Should().Be((DepreciationConvention.DailyProRata, new DateOnly(2026, 4, 21), "1491"));

        // April: AC-1 held 10 of 30 days → 200 × 1/3; AC-2 (1,000 → 100 a month) 33.3333; AC-5 from the 10th, 21 of 30 days → 70.
        DepreciationRunResult april = (await assets.RunDepreciationAsync(companyId, new DateOnly(2026, 4, 30), "AC-DEP-4")).Value;
        april.TotalDepreciation.Should().Be(66.6667m + 33.3333m + 70m);
        (await assets.RunDepreciationAsync(companyId, new DateOnly(2026, 5, 31), "AC-DEP-5")).Value.TotalDepreciation.Should().Be(400m);
    }

    [Fact]
    public void Conventions_set_the_first_month_charged_and_its_share()
    {
        FixedAsset Asset(DepreciationConvention convention, int day) => new(1, "X", "X", "EQP", 1_200m, 0m, new DateOnly(2026, 4, day), DepreciationMethod.StraightLine, 12, 0m, "1", "2", "3")
        {
            Convention = convention,
        };

        Asset(DepreciationConvention.FullMonth, 30).FirstChargePeriod().Should().Be(202604);
        Asset(DepreciationConvention.MidMonth, 15).FirstChargePeriod().Should().Be(202604);
        Asset(DepreciationConvention.MidMonth, 16).FirstChargePeriod().Should().Be(202605);
        Asset(DepreciationConvention.NextMonth, 1).FirstChargePeriod().Should().Be(202605);
        Asset(DepreciationConvention.NextMonth, 1).SkipReasonFor(new DateOnly(2026, 4, 30)).Should().Be(DepreciationSkip.NotYetAcquired);
        Asset(DepreciationConvention.DailyProRata, 16).MonthlyDepreciation(0m, new DateOnly(2026, 4, 30)).Should().Be(50m, "15 of 30 days of 100");
        Asset(DepreciationConvention.DailyProRata, 16).MonthlyDepreciation(0m, new DateOnly(2026, 5, 31)).Should().Be(100m);
    }
}
