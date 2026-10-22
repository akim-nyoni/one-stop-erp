using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the invoice dispute queue: against 10 widgets ordered and received at 5, an invoice at 5.50 (outside 5%) and
/// one for 12 units are queued with their reasons rather than lost; the quantity dispute cannot be accepted, the price
/// one is accepted with a note, posting AP at 55, clearing GRNI at 50 and putting 5 to price variance; the quantity one
/// is rejected. An invoice 2% over on another order matches within tolerance but needs a variance account to post.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class InvoiceDisputeTests
{
    private readonly AccountingFixture _fixture;

    public InvoiceDisputeTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Mismatched_invoices_are_queued_and_decided()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "5190", "Purchase Price Variance", AccountType.Expense));
        Supplier supplier = new(companyId, "ID-SUP", "Pricey Parts", Scenario.Usd);
        Item widget = new(companyId, "ID-W", "Widget");
        Warehouse wh = new(companyId, "IDWH", "Main");
        db.AddRange(supplier, widget, wh);
        await db.SaveChangesAsync();

        IProcurementService pop = scope.ServiceProvider.GetRequiredService<IProcurementService>();
        async Task<(long PoId, long LineId)> OrderAndReceive(string number, decimal quantity, decimal price)
        {
            long poId = (await pop.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
            {
                CompanyId = companyId, SupplierId = supplier.Id, OrderNumber = number, OrderDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd,
                Lines = [new PurchaseOrderLineRequest(widget.Id, "Widget", quantity, price)],
            })).Value.PurchaseOrderId;
            long lineId = await db.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == poId).Select(l => l.Id).SingleAsync();
            (await pop.ReceiveGoodsAsync(new ReceiveGoodsRequest
            {
                CompanyId = companyId, PurchaseOrderId = poId, WarehouseId = wh.Id, GrnNumber = number + "-G", Date = new DateOnly(2026, 3, 5), Lines = [new ReceiveLine(lineId, quantity)],
            })).IsSuccess.Should().BeTrue();
            return (poId, lineId);
        }

        (long poId, long lineId) = await OrderAndReceive("ID-PO1", 10m, 5m);
        IInvoiceDisputeService disputes = scope.ServiceProvider.GetRequiredService<IInvoiceDisputeService>();
        MatchInvoiceRequest Invoice(long order, long line, string number, decimal quantity, decimal price, string? variance = null) => new()
        {
            CompanyId = companyId, PurchaseOrderId = order, InvoiceNumber = number, Date = new DateOnly(2026, 3, 8), TolerancePercent = 5m,
            PriceVarianceAccountCode = variance, Lines = [new InvoiceMatchLine(line, quantity, price)],
        };

        InvoiceSubmission dear = (await disputes.SubmitAsync(Invoice(poId, lineId, "INV-DEAR", 10m, 5.5m))).Value;
        dear.Matched.Should().BeFalse();
        InvoiceSubmission over = (await disputes.SubmitAsync(Invoice(poId, lineId, "INV-OVER", 12m, 5m))).Value;
        (await disputes.OpenAsync(companyId)).Select(d => (d.InvoiceNumber, d.ReasonCode, d.InvoicedValue)).Should().Equal(
            ("INV-DEAR", "Pop.PriceMismatch", 55m),
            ("INV-OVER", "Pop.QuantityMismatch", 60m));

        (await disputes.AcceptAsync(companyId, over.DisputeId!.Value, 3, "Agreed", "5190")).Error.Code.Should().Be("Dispute.Quantity");
        (await disputes.AcceptAsync(companyId, dear.DisputeId!.Value, 3, " ", "5190")).Error.Code.Should().Be("Dispute.NoteRequired");
        (await disputes.AcceptAsync(companyId, dear.DisputeId!.Value, 3, "Price rise agreed by phone", "5190")).Value.InvoiceValue.Should().Be(55m);
        (await disputes.RejectAsync(companyId, over.DisputeId!.Value, 3, "Only 10 delivered; credit note requested")).IsSuccess.Should().BeTrue();
        (await disputes.OpenAsync(companyId)).Should().BeEmpty();

        // Within tolerance but priced differently: the variance needs an account.
        (long po2, long line2) = await OrderAndReceive("ID-PO2", 1m, 100m);
        (await disputes.SubmitAsync(Invoice(po2, line2, "INV-2PC", 1m, 102m))).Error.Code.Should().Be("Pop.NoPriceVarianceAccount");
        (await disputes.SubmitAsync(Invoice(po2, line2, "INV-2PC", 1m, 102m, "5190"))).Value.Matched.Should().BeTrue();

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 3, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Should().NotContain(r => r.AccountCode == "2150" && (r.Debit != 0m || r.Credit != 0m));
        // Both price differences (5 on INV-DEAR, 2 on INV-2PC) are on goods still in stock, so they go onto the stock's value, not the variance account.
        tb.Rows.Should().NotContain(r => r.AccountCode == "5190" && (r.Debit != 0m || r.Credit != 0m));
        tb.Rows.Single(r => r.AccountCode == "1400").Debit.Should().Be(157m);
    }
}
