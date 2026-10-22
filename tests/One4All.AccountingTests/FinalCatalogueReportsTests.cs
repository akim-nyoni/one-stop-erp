using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Restaurant;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.Restaurant;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the last catalogue reports: GL19 period close status, AR44 customer deposits held, INV95 item master with the variant
/// matrix, POS118 tabs, POS119 kitchen volumes with a void after the ticket was sent, POS122 slips at three paper widths and TX223
/// the tax invoice compliance layout.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class FinalCatalogueReportsTests
{
    private readonly AccountingFixture _fixture;

    public FinalCatalogueReportsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Last_catalogue_reports_render()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string report, Dictionary<string, object?> parameters)
        {
            var result = await runner.RenderAsync(companyId, report, parameters, ReportFormat.Csv);
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            return Encoding.UTF8.GetString(result.Value.Content);
        }

        // GL19: every period to the date, each with the close checks.
        string close = await Render("GL19", new() { ["AsAtDate"] = new DateOnly(2026, 2, 15) });
        close.Should().Contain("Ready").And.Contain("2026-01-31").And.Contain("2026-02-28").And.NotContain("2026-03-31");

        // AR44: a deposit received, part of it applied.
        Customer customer = new(companyId, "DEP-C", "Deposit Customer", Scenario.Usd);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "DEP-1", ReceiptDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, Amount = 500m, BankAccountCode = "1100" }))
            .IsSuccess.Should().BeTrue();
        long invoice = (await ar.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "DEP-INV", DocumentDate = new DateOnly(2026, 3, 10), Currency = Scenario.Usd, NetAmount = 200m, SalesAccountCode = "4000",
        })).Value.OpenItemId;
        long deposit = await db.OpenItems.Where(i => i.CompanyId == companyId && i.DocumentNumber == "DEP-1").Select(i => i.Id).SingleAsync();
        (await scope.ServiceProvider.GetRequiredService<IAllocationService>().AllocateAsync(companyId, TradingLedger.AccountsReceivable, invoice, deposit, 200m, new DateOnly(2026, 3, 10)))
            .IsSuccess.Should().BeTrue();
        string deposits = await Render("AR44", new() { ["AsAtDate"] = new DateOnly(2026, 3, 31) });
        deposits.Should().Contain("DEP-C Deposit Customer,DEP-1,2026-03-01,Receipt,USD,500.00,300.00,30,300");
        (await Render("AR44", new() { ["AsAtDate"] = new DateOnly(2026, 3, 5) })).Should().Contain("500.00,500.00,4,500");

        // INV95: a shirt in two sizes and two colours, one combination missing.
        Item shirt = new(companyId, "SHIRT", "Shirt") { Category = "Apparel", Brand = "House" };
        db.Items.Add(shirt);
        await db.SaveChangesAsync();
        foreach ((string size, string colour) in new[] { ("M", "Blue"), ("M", "Red"), ("L", "Blue") })
        {
            db.Items.Add(new Item(companyId, $"SHIRT-{size}-{colour}", $"Shirt {size} {colour}") { ParentItemId = shirt.Id, Size = size, Colour = colour, Category = "Apparel" });
        }

        await db.SaveChangesAsync();
        string master = await Render("INV95", new() { ["Category"] = "Apparel" });
        master.Should().Contain("SHIRT,Shirt,,").And.Contain("Matrix - size M: Blue: SHIRT-M-BLUE  Red: SHIRT-M-RED").And.Contain("Matrix - size L: Blue: SHIRT-L-BLUE  Red: ·")
            .And.Contain("SHIRT-L-BLUE,Shirt L Blue,SHIRT,L,Blue");

        // POS118 / POS119: two tabs, one billed with a tip; a fired line voided afterwards.
        Warehouse kitchen = new(companyId, "FC-KIT", "Kitchen");
        db.Warehouses.Add(kitchen);
        Core.Security.User manager = new($"mgr{Guid.NewGuid():N}"[..12], "Final Manager");
        db.Add(manager);
        Core.Security.Role voiders = new($"Final voiders {companyId}");
        voiders.Grant("Pos.Restaurant", Core.Security.OperationFlags.VoidReverse);
        db.Add(voiders);
        await db.SaveChangesAsync();
        Core.Security.User floorSupervisor = new($"sup{Guid.NewGuid():N}"[..12], "Final Supervisor");
        floorSupervisor.AssignRole(voiders.Id);
        db.Add(floorSupervisor);
        await db.SaveChangesAsync();
        IRestaurantService restaurant = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
        long dish = (await restaurant.CreateMenuItemAsync(companyId, "FC-DISH", "Dish", 30m, "4000", "5000", [])).Value;
        long table = (await restaurant.CreateTableAsync(companyId, "FC1", 4)).Value, table2 = (await restaurant.CreateTableAsync(companyId, "FC2", 2)).Value;
        DateOnly night = new(2026, 4, 10);
        long billed = (await restaurant.OpenOrderAsync(companyId, table, "FC-ORD-1", manager.Id, night, 3)).Value;
        (await restaurant.AddItemAsync(companyId, billed, dish, 2m)).IsSuccess.Should().BeTrue();
        (await restaurant.AddItemAsync(companyId, billed, dish, 1m)).IsSuccess.Should().BeTrue();
        (await restaurant.FireOrderAsync(companyId, billed, "KT-1", night)).IsSuccess.Should().BeTrue();
        long lastLine = (await db.TableOrders.AsNoTracking().Include(o => o.Lines).SingleAsync(o => o.Id == billed)).Lines.OrderBy(l => l.Id).Last().Id;
        (await restaurant.VoidLineAsync(companyId, billed, lastLine, "Dropped", floorSupervisor.Id)).IsSuccess.Should().BeTrue();
        (await restaurant.BillOrderAsync(companyId, billed, kitchen.Id, night, "1000", tipAmount: 5m, tipsPayableAccountCode: "2100", tillSessionId: await RestaurantTill.SessionAsync(scope.ServiceProvider, companyId))).IsSuccess.Should().BeTrue();
        long open = (await restaurant.OpenOrderAsync(companyId, table2, "FC-ORD-2", manager.Id, night, 2)).Value;
        (await restaurant.AddItemAsync(companyId, open, dish, 1m)).IsSuccess.Should().BeTrue();

        string tabs = await Render("POS118", new() { ["FromDate"] = night, ["ToDate"] = night });
        tabs.Should().Contain("Open tab,").And.Contain("FC-ORD-2 @ FC2").And.Contain(",2,,30");
        tabs.Split('\n').Should().Contain(l => l.StartsWith("Waiter,") && l.Contains("1 still open,,2,") && l.TrimEnd().EndsWith(",60.00,5.00"));
        string kitchenReport = await Render("POS119", new() { ["FromDate"] = night, ["ToDate"] = night });
        kitchenReport.Should().Contain("2026-04-10,All courses,1,3,0,1,Dropped,30");

        // POS122 / TX223 from a till sale.
        Branch branch = new(companyId, "FC-BR", "Shop");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        PosTerminal terminal = new(companyId, branch.Id, "FC-TILL", "Till");
        Item soap = new(companyId, "FC-SOAP", "Soap bar") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Warehouse shop = new(companyId, "FC-WH", "Shop floor");
        db.AddRange(terminal, soap, shop);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IInventoryService>().ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = soap.Id, WarehouseId = shop.Id, Date = new DateOnly(2026, 6, 1), Quantity = 10m, UnitCost = 1m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();
        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        long session = (await pos.OpenTillAsync(companyId, terminal.Id, cashierUserId: 1, openingFloat: 0m)).Value;
        (await pos.CompleteSaleAsync(new CompleteSaleRequest
        {
            CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = 1, SaleNumber = "FC-S1", SaleDate = new DateOnly(2026, 6, 2), WarehouseId = shop.Id, TaxAccountCode = "2200",
            Lines = [new PosSaleLineRequest(soap.Id, "Soap bar", 2m, 2.5m, 5m, 0.75m, "4000")],
            Tenders = [new PosTenderRequest(TenderType.Cash, Scenario.Usd, 10m, "1000", IsChange: false), new PosTenderRequest(TenderType.Cash, Scenario.Usd, 4.25m, "1000", IsChange: true)],
        })).IsSuccess.Should().BeTrue();

        foreach ((string paper, int width) in new[] { ("58mm", 32), ("80mm", 48), ("A4", 96) })
        {
            string slip = await Render("POS122", new() { ["SaleNumber"] = "FC-S1", ["Paper"] = paper });
            slip.Should().Contain("Soap bar");
            List<string> texts = [.. slip.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(l => l[(l.IndexOf(',') + 1)..].TrimEnd('\r').Trim('"'))];
            texts.Max(t => t.Length).Should().BeLessThanOrEqualTo(width, paper);
        }

        string invoiceLayout = await Render("TX223", new() { ["SaleNumber"] = "FC-S1" });
        invoiceLayout.Should().Contain("TAX INVOICE / FISCAL RECEIPT").And.Contain("FC-S1").And.Contain("Soap bar").And.Contain("5.00 / 0.75 / 5.75 USD");
        invoiceLayout.Should().Contain("Document number unique,1 sale(s) with this number,PASS").And.Contain("Every line has a tax code").And.Contain("Payment covers the total");
        invoiceLayout.Split('\n').Should().Contain(l => l.Contains("Every line has a tax code") && l.Contains("FAIL"));
        (await Render("TX223", new() { ["SaleNumber"] = "NOPE" })).Should().NotContain("TAX INVOICE");
    }
}
