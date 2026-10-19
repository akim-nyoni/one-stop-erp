using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Inventory;
using One4All.Core.SalesOrders;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Goods bought at 2.00, despatched and invoiced at 5.00 plus 15% VAT, come back after a later purchase at 4.00 moved the average: the credit note is
/// itemised at the invoiced price and tax, the goods return at the 2.00 they went out at, and no more can come back than was invoiced.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SalesReturnAfterInvoiceTests
{
    private readonly AccountingFixture _fixture;

    public SalesReturnAfterInvoiceTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Invoiced_goods_come_back_on_a_credit_note_at_their_original_cost()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer customer = new(companyId, "RI-C", "Buyer", Scenario.Usd);
        Item item = new(companyId, "RI-GADGET", "Gadget") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Warehouse warehouse = new(companyId, "RIWH", "Main");
        db.AddRange(customer, item, warehouse);
        await db.SaveChangesAsync();

        IInventoryService inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        (await inventory.ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = item.Id, WarehouseId = warehouse.Id, Date = new DateOnly(2026, 4, 1), Quantity = 100m, UnitCost = 2.00m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();
        ISalesOrderService orders = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
        long orderId = (await orders.CreateOrderAsync(new CreateSalesOrderRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, OrderNumber = "RI-SO1", OrderDate = new DateOnly(2026, 4, 2), Currency = Scenario.Usd,
            Lines = [new SalesOrderLineRequest(item.Id, "Gadget", 20m, 5.00m, null, "4000")],
        })).Value.SalesOrderId;
        long lineId = await db.Set<SalesOrderLine>().Where(l => l.SalesOrderId == orderId).Select(l => l.Id).SingleAsync();
        (await orders.DespatchAsync(new DespatchRequest
        {
            CompanyId = companyId, SalesOrderId = orderId, WarehouseId = warehouse.Id, Date = new DateOnly(2026, 4, 2), Reference = "RI-DN1", Lines = [new DespatchLine(lineId, 20m)],
        })).IsSuccess.Should().BeTrue();
        (await orders.InvoiceAsync(new InvoiceOrderRequest
        {
            CompanyId = companyId, SalesOrderId = orderId, InvoiceNumber = "RI-INV1", Date = new DateOnly(2026, 4, 3), TaxAccountCode = "2200", Lines = [new InvoiceOrderLine(lineId, 20m, 15m)],
        })).IsSuccess.Should().BeTrue();
        (await inventory.ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = item.Id, WarehouseId = warehouse.Id, Date = new DateOnly(2026, 4, 4), Quantity = 100m, UnitCost = 4.00m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();

        SalesReturnRequest Return(string number, decimal quantity, decimal tax) => new()
        {
            CompanyId = companyId, SalesOrderId = orderId, CreditNoteNumber = number, Date = new DateOnly(2026, 4, 10), WarehouseId = warehouse.Id, Reason = "Faulty", TaxAccountCode = "2200",
            Lines = [new SalesReturnLine(lineId, quantity, tax)],
        };
        (await orders.ReturnInvoicedAsync(Return("RI-CN0", 21m, 0m))).Error.Code.Should().Be("Sop.NotReturnable");
        SalesReturnResult back = (await orders.ReturnInvoicedAsync(Return("RI-CN1", 5m, 3.75m))).Value;
        (back.CreditNoteNumbers.Single(), back.CreditValue, back.RestockedCost).Should().Be(("RI-CN1", 28.75m, 10m));
        (await orders.ReturnInvoicedAsync(Return("RI-CN2", 16m, 0m))).Error.Code.Should().Be("Sop.NotReturnable");
        (await db.Set<SalesOrderLine>().AsNoTracking().SingleAsync(l => l.Id == lineId)).ReturnedQuantity.Should().Be(5m);

        TrialBalanceReport tb = await scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>().TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
        tb.IsBalanced.Should().BeTrue();
        decimal Net(string code) => tb.Rows.Where(r => r.AccountCode == code).Sum(r => r.Debit - r.Credit);
        Net("5000").Should().Be(30m, "40 despatched less 10 back at the original cost");
        Net("4000").Should().Be(-75m, "100 invoiced less 25 credited");
        (await db.OpenItems.AsNoTracking().Where(o => o.CompanyId == companyId && o.Ledger == TradingLedger.AccountsReceivable && o.TraderId == customer.Id).SumAsync(o => o.Sign == LedgerSign.Debit ? o.OutstandingTransaction : -o.OutstandingTransaction))
            .Should().Be(115m - 28.75m);
    }
}
