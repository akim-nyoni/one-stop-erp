using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.FixedAssets;
using One4All.Application.Abstractions.MasterData;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.FixedAssets;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// A supplier refunds an overpayment: banked against the supplier and matched to the payment it reverses. A van is sold to a customer: invoiced at
/// 1,000 plus 15% VAT, the proceeds account clears to nil, and the loss is on the carrying amount.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SupplierRefundAndAssetSaleTests
{
    private readonly AccountingFixture _fixture;

    public SupplierRefundAndAssetSaleTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Supplier_refunds_are_banked_and_assets_sold_on_a_vat_invoice()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "1890", "Asset Disposal Proceeds", AccountType.Asset));
        Supplier supplier = new(companyId, "SRA-S", "Supplier", Scenario.Usd);
        Customer customer = new(companyId, "SRA-C", "Van Buyer", Scenario.Usd);
        db.AddRange(supplier, customer);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IMasterDataService>().CreateTaxCodeAsync(new NewTaxCode(companyId, "SRA-STD", "Standard", TaxType.Standard, 15m,
            new DateOnly(2026, 1, 1), "2200", "2210"))).IsSuccess.Should().BeTrue();

        // The supplier was paid 100 twice over; the refund comes back and clears the overpayment.
        IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
        long overpaid = (await ap.PostPaymentAsync(new PaymentRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = "SRA-PAY", PaymentDate = new DateOnly(2026, 2, 1), Currency = Scenario.Usd, Amount = 100m, BankAccountCode = "1100",
        })).Value.OpenItemId;
        (await ap.PostRefundReceiptAsync(companyId, supplier.Id, "SRA-REF0", new DateOnly(2026, 2, 10), Scenario.Usd, 100m, "1100", "NOPE")).Error.Code.Should().Be("Ap.RefundAgainst");
        long refund = (await ap.PostRefundReceiptAsync(companyId, supplier.Id, "SRA-REF", new DateOnly(2026, 2, 10), Scenario.Usd, 100m, "1100", "SRA-PAY")).Value.OpenItemId;
        (await db.OpenItems.AsNoTracking().Where(o => o.Id == overpaid || o.Id == refund).SumAsync(o => o.OutstandingTransaction)).Should().Be(0m);

        // A 1,200 van (100 a month) sold on 20 January for 1,000 plus VAT: January is charged, so a loss of 100 on 1,100.
        IFixedAssetService fa = scope.ServiceProvider.GetRequiredService<IFixedAssetService>();
        long van = (await fa.RegisterAsync(new RegisterAssetRequest
        {
            CompanyId = companyId, AssetNumber = "SRA-VAN", Description = "Van", AssetClass = "Vehicles", Cost = 1_200m, ResidualValue = 0m, AcquisitionDate = new DateOnly(2026, 1, 5),
            Method = DepreciationMethod.StraightLine, UsefulLifeMonths = 12, CostAccountCode = "1600", AccumulatedDepreciationAccountCode = "1650", DepreciationExpenseAccountCode = "6000",
            FundingAccountCode = "1100", DocumentNumber = "ACQ-SRA-VAN", Convention = DepreciationConvention.FullMonth,
        })).Value;
        DisposalResult sold = (await fa.SellToCustomerAsync(new AssetSaleRequest
        {
            CompanyId = companyId, AssetId = van, CustomerId = customer.Id, NetProceeds = 1_000m, TaxCode = "sra-std", Date = new DateOnly(2026, 1, 20), InvoiceNumber = "SRA-INV",
            ProceedsAccountCode = "1890", GainAccountCode = "4900", LossAccountCode = "6900",
        })).Value;
        sold.GainOrLoss.Should().Be(-100m);
        (await db.OpenItems.AsNoTracking().SingleAsync(o => o.CompanyId == companyId && o.DocumentNumber == "SRA-INV")).OutstandingTransaction.Should().Be(1_150m);

        TrialBalanceReport tb = await scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>().TrialBalanceAsync(companyId, new DateOnly(2026, 2, 28));
        tb.IsBalanced.Should().BeTrue();
        decimal Net(string code) => tb.Rows.Where(r => r.AccountCode == code).Sum(r => r.Debit - r.Credit);
        (Net("1890"), Net("2200"), Net("1600")).Should().Be((0m, -150m, 0m));
    }
}
