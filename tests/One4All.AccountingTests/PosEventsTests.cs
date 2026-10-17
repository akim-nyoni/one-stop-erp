using System.Text;
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
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Reporting;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the till event log and refunds (brief §14 F): a partial refund posts revenue, tax and cash back and restocks at the
/// original cost; over-refunds and unauthorised refunds are refused; voids, discounts, overrides, no-sales, parking, loyalty,
/// reprints and fiscal submissions are recorded and read back by POS106–111, 115, 116 and 121.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PosEventsTests
{
    private readonly AccountingFixture _fixture;

    public PosEventsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Till_events_and_refunds_are_recorded_and_reported()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        Branch branch = new(companyId, "EHO", "Head Office");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        PosTerminal terminal = new(companyId, branch.Id, "ETILL", "Event Till");
        Item soda = new(companyId, "ESODA", "Soda 500ml") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Warehouse floor = new(companyId, "EWH", "Shop Floor");
        db.AddRange(terminal, soda, floor);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IInventoryService>().ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = soda.Id, WarehouseId = floor.Id, Date = new DateOnly(2026, 3, 1), Quantity = 20m, UnitCost = 1.00m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();

        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        long session = (await pos.OpenTillAsync(companyId, terminal.Id, cashierUserId: 1, openingFloat: 100m)).Value;
        async Task<long> Sell(string number, decimal quantity)
            => (await pos.CompleteSaleAsync(new CompleteSaleRequest
            {
                CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = 1, SaleNumber = number, SaleDate = new DateOnly(2026, 3, 2), WarehouseId = floor.Id,
                TaxAccountCode = "2200", Lines = [new PosSaleLineRequest(soda.Id, "Soda 500ml", quantity, 2.00m, quantity * 2m, quantity * 0.3m, "4000")],
                Tenders = [new PosTenderRequest(TenderType.Cash, Scenario.Usd, quantity * 2.3m, "1000", IsChange: false)],
            })).Value.SaleId;
        long saleId = await Sell("ES-1", 10m);
        long voided = await Sell("ES-2", 1m);
        (await pos.VoidSaleAsync(companyId, voided, new DateOnly(2026, 3, 2), "Wrong item rung", "2200")).IsSuccess.Should().BeTrue();

        IPosEventService events = scope.ServiceProvider.GetRequiredService<IPosEventService>();
        long lineId = (await db.Sales.AsNoTracking().Include(s => s.Lines).SingleAsync(s => s.Id == saleId)).Lines[0].Id;
        PosRefundRequest Refund(decimal quantity, long? authoriser = 2) => new()
        {
            CompanyId = companyId, SaleId = saleId, TerminalId = terminal.Id, CashierUserId = 1, AuthorisedByUserId = authoriser, Date = new DateOnly(2026, 3, 3), RefundAccountCode = "1000",
            TaxAccountCode = "2200", Reason = "Damaged", Lines = [new PosRefundLine(lineId, quantity)], Restock = true,
        };
        (await events.RefundAsync(Refund(3m, authoriser: null))).Error.Code.Should().Be("Pos.AuthorisationRequired");
        (await events.RefundAsync(Refund(3m, authoriser: 1))).Error.Code.Should().Be("Pos.SelfAuthorised");
        PosRefundResult refund = (await events.RefundAsync(Refund(3m))).Value;
        refund.Gross.Should().Be(6.90m);
        (await events.RefundAsync(Refund(8m))).Error.Code.Should().Be("Pos.RefundExceedsSale"); // 7 left

        var journal = await db.Journals.AsNoTracking().Include(j => j.Lines).SingleAsync(j => j.CompanyId == companyId && j.JournalNumber == refund.JournalNumber);
        journal.Lines.Select(l => (l.AccountCode, l.BaseDebit, l.BaseCredit)).Should().BeEquivalentTo([("4000", 6.00m, 0m), ("2200", 0.90m, 0m), ("1000", 0m, 6.90m)]);
        (await db.Set<StockMovement>().AsNoTracking().SingleAsync(m => m.CompanyId == companyId && m.Reference == "REFUND:ES-1")).Value.Should().Be(3.00m);

        async Task Record(PosEventKind kind, string description, decimal amount, decimal? original = null, string? reason = null, long? authoriser = null, string? sale = null,
            string? customer = null, string? reference = null, string? outcome = null)
            => (await events.RecordAsync(new PosEventRequest
            {
                CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = 1, Kind = kind, Date = new DateOnly(2026, 3, 2), Description = description, Amount = amount,
                OriginalAmount = original, Reason = reason, AuthorisedByUserId = authoriser, SaleNumber = sale, CustomerReference = customer, Reference = reference, Outcome = outcome,
            })).IsSuccess.Should().BeTrue();
        (await events.RecordAsync(new PosEventRequest { CompanyId = companyId, TerminalId = terminal.Id, CashierUserId = 1, Kind = PosEventKind.NoSale, Date = new DateOnly(2026, 3, 2) }))
            .Error.Code.Should().Be("Pos.ReasonRequired");
        await Record(PosEventKind.LineVoid, "Chips", 1.50m, reason: "Customer changed mind", sale: "ES-1");
        await Record(PosEventKind.LineDiscount, "Soda 500ml", 1.00m, original: 4.00m, reason: "Dented can", sale: "ES-1");
        await Record(PosEventKind.PriceOverride, "Soda 500ml", 1.50m, original: 2.00m, reason: "Price match", authoriser: 2, sale: "ES-1");
        await Record(PosEventKind.NoSale, "Drawer", 0m, reason: "Change for float", authoriser: 2);
        await Record(PosEventKind.ParkedSale, "Basket", 12m, sale: "ES-P1");
        await Record(PosEventKind.ResumedSale, "Basket", 12m, sale: "ES-P1");
        await Record(PosEventKind.ParkedSale, "Basket", 8m, sale: "ES-P2");
        await Record(PosEventKind.Reprint, "Receipt", 23m, reason: "Customer copy", sale: "ES-1");
        await Record(PosEventKind.FiscalSubmission, "Receipt", 23m, sale: "ES-1", reference: "FDMS-0001", outcome: "Accepted");
        await Record(PosEventKind.LoyaltyEarned, "Points", 10m, customer: "CARD-77", sale: "ES-1");
        await Record(PosEventKind.LoyaltyRedeemed, "Points", 4m, customer: "CARD-77");

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            Dictionary<string, object?> parameters = new() { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 12, 31) };
            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("POS106")).Should().Contain(",Sale,ES-2,Sale ES-2,Wrong item rung,-,2.3").And.Contain(",Line,ES-1,Chips,Customer changed mind,-,1.5");
        (await Render("POS107")).Should().Contain($",ES-1,Soda 500ml,3,Damaged,").And.Contain($",{refund.JournalNumber},Restocked,6.9");
        (await Render("POS108")).Should().Contain(",ES-1,Soda 500ml,4.00,25.0%,Dented can,1");
        (await Render("POS109")).Should().Contain(",ES-1,Soda 500ml,2.00,1.50,Price match,-0.5");
        (await Render("POS110")).Should().Contain(",ETILL,").And.Contain(",Change for float,");
        string parked = await Render("POS111");
        parked.Should().Contain(",ES-P1,Resumed,").And.Contain(",ES-P2,Abandoned,-,-,8");
        (await Render("POS115")).Should().Contain("CARD-77,2,10,4,2026-03-02,6");
        (await Render("POS116")).Should().Contain(",ES-1,Customer copy,23");
        (await Render("POS121")).Should().Contain(",ES-1,FDMS-0001,Accepted,23");

        string overrides = await Render("SYS241");
        overrides.Should().Contain(",POS,PriceOverride,ES-1: Soda 500ml 2.00 → 1.50,").And.Contain(",POS,Refund,ES-1: Soda 500ml refunded 6.90,").And.Contain(",POS,NoSale,Drawer opened without a sale,");

        foreach (string code in RetailReports.All().Select(r => r.Code))
        {
            await Render(code);
        }
    }
}
