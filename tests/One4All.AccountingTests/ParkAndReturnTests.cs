using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers parked sales (held under a tag, resumed at another till once, discarded with a reason) and refunds without a receipt
/// (supervised, within the company's limit, revenue and tax reversed and the goods restocked at average cost).
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ParkAndReturnTests
{
    private readonly AccountingFixture _fixture;

    public ParkAndReturnTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Sales_are_parked_and_goods_returned_without_a_receipt()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Branch branch = new(companyId, "PK", "Cafe");
        db.Branches.Add(branch);
        Item mug = new(companyId, "PK-MUG", "Mug") { RevenueAccountCode = "4000", CostOfSalesAccountCode = "5000" };
        Warehouse shop = new(companyId, "PK-WH", "Shop");
        db.AddRange(mug, shop);
        await db.SaveChangesAsync();
        PosTerminal till1 = new(companyId, branch.Id, "PK1", "Till 1"), till2 = new(companyId, branch.Id, "PK2", "Till 2");
        db.PosTerminals.AddRange(till1, till2);
        await db.SaveChangesAsync();

        // Park, list, resume elsewhere once, discard with a reason.
        IParkedSaleService parked = scope.ServiceProvider.GetRequiredService<IParkedSaleService>();
        PosSaleLineRequest[] tableFour = [new PosSaleLineRequest(mug.Id, "Mug", 2m, 10m, 20m, 3m, "4000")];
        long first = (await parked.ParkAsync(companyId, till1.Id, "Table 4", 1, tableFour)).Value;
        long second = (await parked.ParkAsync(companyId, till1.Id, "Mr Smith", 1, [new PosSaleLineRequest(mug.Id, "Mug", 1m, 10m, 10m, 1.5m, "4000")])).Value;
        (await parked.ParkAsync(companyId, till1.Id, " ", 1, tableFour)).Error.Code.Should().Be("Pos.ParkInvalid");
        (await parked.ParkedAsync(companyId)).Select(p => (p.Tag, p.Total)).Should().Equal(("Table 4", 23m), ("Mr Smith", 11.5m));
        (await parked.ResumeAsync(companyId, first, till2.Id, 2)).Value.Should().Equal(tableFour);
        (await parked.ResumeAsync(companyId, first, till2.Id, 2)).Error.Code.Should().Be("Pos.ParkedClosed");
        (await parked.DiscardAsync(companyId, second, 1, " ")).Error.Code.Should().Be("Pos.ReasonRequired");
        (await parked.DiscardAsync(companyId, second, 1, "Customer left")).IsSuccess.Should().BeTrue();
        (await parked.ParkedAsync(companyId)).Should().BeEmpty();

        // A mug brought back without a receipt.
        (await scope.ServiceProvider.GetRequiredService<IInventoryService>().ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = mug.Id, WarehouseId = shop.Id, Date = new DateOnly(2026, 8, 1), Quantity = 10m, UnitCost = 4m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();
        IPosEventService events = scope.ServiceProvider.GetRequiredService<IPosEventService>();
        UnreceiptedRefundRequest Return(decimal quantity, long? supervisor = 2) => new()
        {
            CompanyId = companyId, TerminalId = till1.Id, CashierUserId = 1, AuthorisedByUserId = supervisor, Date = new DateOnly(2026, 8, 5), ItemId = mug.Id, Quantity = quantity, UnitPrice = 10m,
            TaxAmount = quantity * 1.5m, TaxAccountCode = "2200", RefundAccountCode = "1000", Reason = "Gift duplicate", RestockWarehouseId = shop.Id,
        };
        (await events.RefundWithoutReceiptAsync(Return(2m))).Error.Code.Should().Be("Pos.UnreceiptedNotAllowed");
        Company company = await db.Companies.SingleAsync(c => c.Id == companyId);
        company.UnreceiptedRefundLimit = 50m;
        await db.SaveChangesAsync();
        (await events.RefundWithoutReceiptAsync(Return(2m, supervisor: 1))).Error.Code.Should().Be("Pos.AuthorisationRequired");
        (await events.RefundWithoutReceiptAsync(Return(5m))).Error.Code.Should().Be("Pos.UnreceiptedLimit");
        (await events.RefundWithoutReceiptAsync(Return(2m))).Value.Gross.Should().Be(23m);

        (await db.StockBalances.AsNoTracking().SingleAsync(b => b.ItemId == mug.Id && b.WarehouseId == shop.Id)).QuantityOnHand.Should().Be(12m);
        TrialBalanceReport tb = await scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>().TrialBalanceAsync(companyId, new DateOnly(2026, 8, 31));
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Where(r => r.AccountCode == "4000").Sum(r => r.Debit - r.Credit).Should().Be(20m);
        tb.Rows.Where(r => r.AccountCode == "2200").Sum(r => r.Debit - r.Credit).Should().Be(3m);
    }
}
