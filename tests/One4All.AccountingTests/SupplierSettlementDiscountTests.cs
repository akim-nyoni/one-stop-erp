using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class SupplierSettlementDiscountTests
{
    private readonly AccountingFixture _fixture;

    public SupplierSettlementDiscountTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Paying_early_with_a_discount_clears_the_supplier_invoice()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long supplierId;
        long invoiceItemId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Supplier supplier = new(companyId, "SSD-SUP", "Prompt Vendor", Scenario.Usd);
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            supplierId = supplier.Id;

            IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
            invoiceItemId = (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest
            {
                CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "SSD-1", DocumentDate = new DateOnly(2026, 3, 1),
                Currency = Scenario.Usd, NetAmount = 500m, ExpenseAccountCode = "5000",
            })).Value.OpenItemId;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();

            (await ap.PayWithDiscountAsync(companyId, invoiceItemId, new DateOnly(2026, 3, 5), 495m, 20m, "1100", "4950", "SSD-P0")).IsFailure.Should().BeTrue();

            var pay = await ap.PayWithDiscountAsync(companyId, invoiceItemId, new DateOnly(2026, 3, 5), 485m, 15m, "1100", "4950", "SSD-P1");
            pay.IsSuccess.Should().BeTrue(pay.IsFailure ? pay.Error.ToString() : null);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            OpenItem invoice = await db.OpenItems.FirstAsync(i => i.Id == invoiceItemId);
            invoice.OutstandingTransaction.Should().Be(0m);

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "1100" && r.Credit == 485m);  // cash paid
            tb.Rows.Should().Contain(r => r.AccountCode == "4950" && r.Credit == 15m);   // discount received income

            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            AgeingReport creditors = await reports.AgedCreditorsAsync(companyId, new DateOnly(2026, 3, 31));
            creditors.Rows.Should().NotContain(r => r.TraderCode == "SSD-SUP");
        }
    }
}
