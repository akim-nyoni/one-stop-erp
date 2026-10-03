using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Procurement;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers purchase order amendments: an order for 10 at 5 with 4 received cannot go below 4; raising it to 12 (20% more
/// than the 10% threshold) makes version 2 and holds it, so nothing more can be received until someone other than the
/// amender re-approves it; the history keeps version 1; a fully invoiced order is closed to amendment; a 5% increase on
/// another order needs no re-approval.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PurchaseOrderAmendmentTests
{
    private readonly AccountingFixture _fixture;

    public PurchaseOrderAmendmentTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Amendments_keep_history_and_large_increases_need_reapproval()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Supplier supplier = new(companyId, "AM-SUP", "Amended Supplies", Scenario.Usd);
        Item widget = new(companyId, "AM-W", "Widget");
        Warehouse wh = new(companyId, "AMWH", "Main");
        db.AddRange(supplier, widget, wh);
        await db.SaveChangesAsync();

        IProcurementService pop = scope.ServiceProvider.GetRequiredService<IProcurementService>();
        async Task<(long PoId, long LineId)> Order(string number, decimal quantity, decimal price)
        {
            long poId = (await pop.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
            {
                CompanyId = companyId, SupplierId = supplier.Id, OrderNumber = number, OrderDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd,
                Lines = [new PurchaseOrderLineRequest(widget.Id, "Widget", quantity, price)],
            })).Value.PurchaseOrderId;
            return (poId, await db.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == poId).Select(l => l.Id).SingleAsync());
        }

        Task<One4All.Core.Common.Result<GrnResult>> Receive(long poId, long lineId, string grn, decimal quantity) => pop.ReceiveGoodsAsync(new ReceiveGoodsRequest
        {
            CompanyId = companyId, PurchaseOrderId = poId, WarehouseId = wh.Id, GrnNumber = grn, Date = new DateOnly(2026, 3, 5), Lines = [new ReceiveLine(lineId, quantity)],
        });

        (long poId, long lineId) = await Order("AM-PO1", 10m, 5m);
        (await Receive(poId, lineId, "AM-G1", 4m)).IsSuccess.Should().BeTrue();

        IPurchaseOrderAmendmentService amendments = scope.ServiceProvider.GetRequiredService<IPurchaseOrderAmendmentService>();
        AmendmentRequest Amend(long line, decimal quantity, decimal price, long user = 1) => new(new DateOnly(2026, 3, 6), user, "Extra units for the new office", [new AmendedLine(line, quantity, price)]);
        (await amendments.AmendAsync(companyId, poId, Amend(lineId, 3m, 5m))).Error.Code.Should().Be("Pop.AmendQuantity");
        (await amendments.AmendAsync(companyId, poId, Amend(lineId, 12m, 5m))).Value.Should().Be(new AmendmentResult(2, 50m, 60m, true));

        (await Receive(poId, lineId, "AM-G2", 8m)).Error.Code.Should().Be("Pop.AwaitingReapproval");
        (await amendments.ReapproveAsync(companyId, poId, 1)).Error.Code.Should().Be("Pop.SelfApproval");
        (await amendments.ReapproveAsync(companyId, poId, 2)).IsSuccess.Should().BeTrue();
        (await Receive(poId, lineId, "AM-G2", 8m)).IsSuccess.Should().BeTrue();

        (await amendments.HistoryAsync(companyId, poId)).Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Version = 1, TotalNet = 50m, Reason = "Extra units for the new office", AmendedByUserId = 1L, Lines = new[] { new AmendedLine(lineId, 10m, 5m) },
        });

        (await pop.MatchInvoiceAsync(new MatchInvoiceRequest
        {
            CompanyId = companyId, PurchaseOrderId = poId, InvoiceNumber = "AM-INV", Date = new DateOnly(2026, 3, 8), Lines = [new InvoiceMatchLine(lineId, 12m, 5m)],
        })).IsSuccess.Should().BeTrue();
        (await amendments.AmendAsync(companyId, poId, Amend(lineId, 12m, 4.5m))).Error.Code.Should().Be("Pop.AmendClosed");

        (long po2, long line2) = await Order("AM-PO2", 100m, 1m);
        (await amendments.AmendAsync(companyId, po2, Amend(line2, 105m, 1m))).Value.AwaitingReapproval.Should().BeFalse();
        (await Receive(po2, line2, "AM-G3", 105m)).IsSuccess.Should().BeTrue();
    }
}
