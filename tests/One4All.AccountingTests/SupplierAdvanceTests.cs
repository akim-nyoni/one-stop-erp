using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Advances to suppliers (brief §4.4): paid into an advances account ahead of the invoice, applied to the invoice when it
/// arrives (a purchase-ledger payment item allocated to it) and refunded when the order falls through.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SupplierAdvanceTests
{
    private const string Advances = "1310"; // Advances to Suppliers
    private const string Bank = "1100";
    private const string ApControl = "2100";

    private readonly AccountingFixture _fixture;

    public SupplierAdvanceTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Advance_is_paid_applied_to_the_invoice_and_the_rest_refunded()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long supplierId;
        long heldId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
            db.Accounts.Add(new LedgerAccount(companyId, Advances, "Advances to Suppliers", AccountType.Asset));
            Supplier supplier = new(companyId, "ADV-S", "Advance Supplier", Scenario.Usd);
            Supplier held = new(companyId, "ADV-H", "Held Supplier", Scenario.Usd) { OnHold = true };
            db.Suppliers.AddRange(supplier, held);
            await db.SaveChangesAsync();
            (supplierId, heldId) = (supplier.Id, held.Id);
        }

        long advanceId;
        long invoiceId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISupplierAdvanceService advances = scope.ServiceProvider.GetRequiredService<ISupplierAdvanceService>();
            IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();

            // A held supplier gets no advance; a bad account is refused.
            (await advances.PayAsync(new SupplierAdvanceRequest(companyId, heldId, "ADV-0", new DateOnly(2026, 5, 1), Scenario.Usd, 100m, Advances, Bank))).Error.Code.Should().Be("Ap.Advance.SupplierHeld");
            (await advances.PayAsync(new SupplierAdvanceRequest(companyId, supplierId, "ADV-0", new DateOnly(2026, 5, 1), Scenario.Usd, 100m, "9999", Bank))).Error.Code.Should().Be("Ap.Advance.Account");

            // 300 paid ahead of purchase order PO-77.
            var paid = await advances.PayAsync(new SupplierAdvanceRequest(companyId, supplierId, "ADV-1", new DateOnly(2026, 5, 1), Scenario.Usd, 300m, Advances, Bank, "PO-77"));
            paid.IsSuccess.Should().BeTrue(paid.IsFailure ? paid.Error.ToString() : null);
            paid.Value.AmountBase.Should().Be(300m);
            advanceId = paid.Value.AdvanceId;
            (await advances.PayAsync(new SupplierAdvanceRequest(companyId, supplierId, "ADV-1", new DateOnly(2026, 5, 1), Scenario.Usd, 300m, Advances, Bank))).Error.Code.Should().Be("Ap.Advance.Duplicate");

            // The invoice for 500 arrives; nothing can be applied before the advance was paid, and 200 of the advance is applied.
            var invoice = await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "ADV-INV", DocumentDate = new DateOnly(2026, 5, 10), Currency = Scenario.Usd, NetAmount = 500m, ExpenseAccountCode = "6800" });
            invoice.IsSuccess.Should().BeTrue(invoice.IsFailure ? invoice.Error.ToString() : null);
            invoiceId = invoice.Value.OpenItemId;
            (await advances.ApplyAsync(companyId, advanceId, invoiceId, new DateOnly(2026, 4, 30))).Error.Code.Should().Be("Ap.Advance.Date");
            (await advances.ApplyAsync(companyId, advanceId, invoiceId, new DateOnly(2026, 5, 10), 600m)).Error.Code.Should().Be("Ap.Advance.ApplyAmount");
            var applied = await advances.ApplyAsync(companyId, advanceId, invoiceId, new DateOnly(2026, 5, 10), 200m);
            applied.IsSuccess.Should().BeTrue(applied.IsFailure ? applied.Error.ToString() : null);
            applied.Value.Applied.Should().Be(200m);
            applied.Value.AdvanceOutstanding.Should().Be(100m);
            applied.Value.InvoiceOutstanding.Should().Be(300m);

            // The order is cut back, so the supplier refunds the 100 left; the advance is then spent.
            (await advances.RefundAsync(companyId, advanceId, new DateOnly(2026, 5, 20), Bank, "ADV-RF", 150m)).Error.Code.Should().Be("Ap.Advance.RefundAmount");
            var refund = await advances.RefundAsync(companyId, advanceId, new DateOnly(2026, 5, 20), Bank, "ADV-RF");
            refund.IsSuccess.Should().BeTrue(refund.IsFailure ? refund.Error.ToString() : null);
            (await advances.ApplyAsync(companyId, advanceId, invoiceId, new DateOnly(2026, 5, 21))).Error.Code.Should().Be("Ap.Advance.Spent");

            IReadOnlyList<SupplierAdvanceRow> rows = await advances.ListAsync(companyId, supplierId);
            rows.Should().ContainSingle(r => r.DocumentNumber == "ADV-1" && r.Amount == 300m && r.Applied == 200m && r.Refunded == 100m && r.Outstanding == 0m
                && r.Status == SupplierAdvanceStatus.Refunded && r.PurchaseOrderNumber == "PO-77");
            (await advances.ListAsync(companyId, supplierId, openOnly: true)).Should().BeEmpty();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            OpenItem invoice = await db.OpenItems.AsNoTracking().SingleAsync(i => i.Id == invoiceId);
            invoice.OutstandingTransaction.Should().Be(300m);
            OpenItem payment = await db.OpenItems.AsNoTracking().SingleAsync(i => i.CompanyId == companyId && i.DocumentNumber == "ADV-1/A1");
            payment.ItemType.Should().Be(OpenItemType.Payment);
            payment.OutstandingTransaction.Should().Be(0m);

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 5, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().NotContain(r => r.AccountCode == Advances && r.Debit - r.Credit != 0m); // 300 in, 200 applied, 100 refunded
            tb.Rows.Should().Contain(r => r.AccountCode == ApControl && r.Credit == 300m);
            tb.Rows.Should().Contain(r => r.AccountCode == Bank && r.Credit == 200m); // 300 out, 100 back

            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            async Task<string> Render(string code)
            {
                var rendered = await runner.RenderAsync(companyId, code, new Dictionary<string, object?> { ["AsAtDate"] = new DateOnly(2026, 5, 31), ["FromDate"] = new DateOnly(2026, 5, 1), ["ToDate"] = new DateOnly(2026, 5, 31) }, ReportFormat.Csv);
                rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
                return Encoding.UTF8.GetString(rendered.Value.Content);
            }

            (await Render("AP67")).Should().Contain("ADV-S,ADV-1,2026-05-01,PO-77,USD,300.00,200.00,100.00,0.00,Refunded");
            (await Render("AP64")).Should().Contain("Advance Supplier,USD,,Active,300");
        }
    }
}
