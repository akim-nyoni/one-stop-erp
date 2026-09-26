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

[Collection(AccountingCollection.Name)]
public sealed class PurchaseRequisitionTests
{
    private readonly AccountingFixture _fixture;

    public PurchaseRequisitionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Approved_requisition_converts_into_a_purchase_order()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long supplierId;
        long itemId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Supplier supplier = new(companyId, "REQ-SUP", "Parts Supplier", Scenario.Usd);
            db.Suppliers.Add(supplier);
            Item item = new(companyId, "REQ-ITEM", "Bolt") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            supplierId = supplier.Id;
            itemId = item.Id;
        }

        long reqId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPurchaseRequisitionService reqs = scope.ServiceProvider.GetRequiredService<IPurchaseRequisitionService>();

            (await reqs.CreateAsync(companyId, supplierId, "REQ-EMPTY", new DateOnly(2026, 3, 1), Scenario.Usd, [])).IsFailure.Should().BeTrue();

            var create = await reqs.CreateAsync(companyId, supplierId, "REQ-1", new DateOnly(2026, 3, 1), Scenario.Usd,
                [new PurchaseRequisitionLineRequest(itemId, "Bolt", 100m, 2m)]);
            create.IsSuccess.Should().BeTrue(create.IsFailure ? create.Error.ToString() : null);
            reqId = create.Value;

            // Cannot approve a draft (must be submitted first), nor convert before approval.
            (await reqs.ApproveAsync(companyId, reqId, 1)).IsFailure.Should().BeTrue();
            (await reqs.ConvertToPurchaseOrderAsync(companyId, reqId, "PO-REQ1", new DateOnly(2026, 3, 2))).IsFailure.Should().BeTrue();

            (await reqs.SubmitAsync(companyId, reqId)).IsSuccess.Should().BeTrue();
            (await reqs.ApproveAsync(companyId, reqId, approvedByUserId: 7)).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPurchaseRequisitionService reqs = scope.ServiceProvider.GetRequiredService<IPurchaseRequisitionService>();
            var convert = await reqs.ConvertToPurchaseOrderAsync(companyId, reqId, "PO-REQ1", new DateOnly(2026, 3, 2));
            convert.IsSuccess.Should().BeTrue(convert.IsFailure ? convert.Error.ToString() : null);

            // A converted requisition cannot convert again.
            (await reqs.ConvertToPurchaseOrderAsync(companyId, reqId, "PO-REQ2", new DateOnly(2026, 3, 3))).IsFailure.Should().BeTrue();

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            PurchaseRequisition req = await db.Set<PurchaseRequisition>().FirstAsync(r => r.Id == reqId);
            req.Status.Should().Be(PurchaseRequisitionStatus.Converted);
            req.PurchaseOrderId.Should().Be(convert.Value);

            PurchaseOrder po = await db.Set<PurchaseOrder>().Include(o => o.Lines).FirstAsync(o => o.Id == convert.Value);
            po.OrderNumber.Should().Be("PO-REQ1");
            po.Lines.Should().ContainSingle(l => l.ItemId == itemId && l.OrderedQuantity == 100m && l.UnitPrice == 2m);
        }
    }
}
