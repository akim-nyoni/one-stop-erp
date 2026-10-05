using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Reporting;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the PO-numbered procurement reports (brief §14 G): two orders for the same item at rising prices, the first
/// received into quarantine and partly rejected; the reports list, measure and compare them; every PO report renders.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ProcurementReportsTests
{
    private readonly AccountingFixture _fixture;

    public ProcurementReportsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Procurement_reports_reflect_orders_receipts_and_inspections()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        db.Accounts.Add(new LedgerAccount(companyId, "5190", "Purchase Price Variance", AccountType.Expense));
        Supplier supplier = new(companyId, "PR-SUP", "Gadget Co", Scenario.Usd);
        Item gadget = new(companyId, "PR-G", "Gadget") { CostOfSalesAccountCode = "5000" };
        Warehouse quarantine = new(companyId, "PRHOLD", "Inspection hold") { IsQuarantine = true };
        Warehouse store = new(companyId, "PRMAIN", "Main store");
        db.AddRange(supplier, gadget, quarantine, store);
        await db.SaveChangesAsync();

        IProcurementService pop = scope.ServiceProvider.GetRequiredService<IProcurementService>();
        async Task<long> Order(string number, DateOnly date, decimal price) => (await pop.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, OrderNumber = number, OrderDate = date, Currency = Scenario.Usd, Lines = [new PurchaseOrderLineRequest(gadget.Id, "Gadget", 10m, price)],
        })).Value.PurchaseOrderId;
        long first = await Order("PR-PO1", new DateOnly(2026, 3, 1), 5m);
        await Order("PR-PO2", new DateOnly(2026, 3, 20), 6m);
        long lineId = await db.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == first).Select(l => l.Id).SingleAsync();
        long grn = (await pop.ReceiveGoodsAsync(new ReceiveGoodsRequest
        {
            CompanyId = companyId, PurchaseOrderId = first, WarehouseId = quarantine.Id, GrnNumber = "PR-G1", Date = new DateOnly(2026, 3, 5), Lines = [new ReceiveLine(lineId, 10m)],
        })).Value.GrnId;
        (await scope.ServiceProvider.GetRequiredService<IQualityInspectionService>().InspectAsync(companyId, grn,
            new InspectionRequest(new DateOnly(2026, 3, 6), 4, store.Id, "5190", [new InspectionResult(gadget.Id, 8m, 2m, "Cracked casings")]))).IsSuccess.Should().BeTrue();

        PurchaseRequisition requisition = new(companyId, supplier.Id, "PR-RQ1", new DateOnly(2026, 3, 25), Scenario.Usd);
        requisition.AddLine(gadget.Id, "Gadget", 3m, 7m).ExpenseAccountCode = "6000";
        db.Add(requisition);
        await db.SaveChangesAsync();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            Dictionary<string, object?> parameters = new() { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 3, 31), ["AsAtDate"] = new DateOnly(2026, 3, 31) };
            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        string orders = await Render("PO126");
        orders.Should().Contain("PR-PO1,2026-03-01,Gadget Co,Received").And.Contain("PR-PO2,2026-03-20,Gadget Co,Open,—,—,USD,60.00,0.00,60");
        (await Render("PO129")).Should().Contain("PR-G1,2026-03-05,PR-PO1,Gadget Co,PR-G Gadget,10,10,0,0.00,50");
        (await Render("PO130")).Should().Contain("PR-G1,2026-03-06,Gadget Co,PR-G Gadget,8,2,Cracked casings,10");
        (await Render("PO132")).Should().Contain("PR-G Gadget,PR-PO2,2026-03-20,Gadget Co,5.00,6.00,+20.0%,10,10");
        (await Render("PO136")).Should().Contain("PR-RQ1,2026-03-25,6000 (no budget),21.00,0.00,0.00,0.00,0.00,No budget,-21");
        (await Render("PO134")).Should().Contain("Gadget Co,USD,2,2,1,100.0%,110");
        (await Render("PO135")).Should().Contain("Gadget Co,1,4.0,4,4,—");

        FrameworkAgreement framework = new(companyId, supplier.Id, "fa-1", "Gadgets 2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 150m);
        db.Add(framework);
        await db.SaveChangesAsync();
        async Task<Result<PurchaseOrderResult>> CallOff(string number, DateOnly date, decimal quantity) => await pop.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, OrderNumber = number, OrderDate = date, Currency = Scenario.Usd, FrameworkAgreementId = framework.Id,
            Lines = [new PurchaseOrderLineRequest(gadget.Id, "Gadget", quantity, 10m)],
        });
        (await CallOff("PR-PO3", new DateOnly(2026, 3, 25), 10m)).IsSuccess.Should().BeTrue();
        (await CallOff("PR-PO4", new DateOnly(2026, 3, 26), 6m)).Error.Code.Should().Be("Pop.FrameworkExceeded");
        (await CallOff("PR-PO5", new DateOnly(2027, 1, 5), 1m)).Error.Code.Should().Be("Pop.FrameworkOutOfTerm");
        (await Render("PO133")).Should().Contain("FA-1,Gadget Co,2026-01-01 to 2026-12-31,150.00,1,100.00,66.7%,Live,50");

        foreach (string code in ProcurementReports.All().Select(r => r.Code))
        {
            await Render(code);
        }
    }
}
