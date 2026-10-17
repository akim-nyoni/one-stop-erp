using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Documents;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the standard document layouts (brief §14 S, B 50, C 67, E 94, H 139): a quotation, an order confirmation, delivery and packing
/// notes, a requisition, a goods received note and shelf labels, each built from its records.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class DocumentLayoutTests
{
    private readonly AccountingFixture _fixture;

    public DocumentLayoutTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Business_documents_render_from_their_records()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer customer = new(companyId, "DL-C", "Builder Ltd", Scenario.Usd);
        Supplier supplier = new(companyId, "DL-S", "Brick Co", Scenario.Usd);
        Item brick = new(companyId, "DL-BRK", "Face brick") { CostOfSalesAccountCode = "5000", UnitWeightKg = 2.5m, Barcode = "6001234" };
        Warehouse yard = new(companyId, "DL-WH", "Yard");
        db.AddRange(customer, supplier, brick, yard);
        await db.SaveChangesAsync();

        IProcurementService procurement = scope.ServiceProvider.GetRequiredService<IProcurementService>();
        long po = (await procurement.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, OrderNumber = "DL-PO1", OrderDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, Lines = [new PurchaseOrderLineRequest(brick.Id, "Face brick", 1_000m, 0.5m)],
        })).Value.PurchaseOrderId;
        long poLine = db.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == po).Select(l => l.Id).Single();
        (await procurement.ReceiveGoodsAsync(new ReceiveGoodsRequest { CompanyId = companyId, PurchaseOrderId = po, WarehouseId = yard.Id, GrnNumber = "DL-GRN1", Date = new DateOnly(2026, 3, 2), Lines = [new ReceiveLine(poLine, 1_000m)] }))
            .IsSuccess.Should().BeTrue();
        PurchaseRequisition requisition = new(companyId, supplier.Id, "DL-RQ1", new DateOnly(2026, 3, 3), Scenario.Usd);
        requisition.AddLine(brick.Id, "Face brick", 500m, 0.55m).ExpenseAccountCode = "6000";
        PriceList retail = new(companyId, "RETAIL", "Retail", Scenario.Usd);
        retail.SetPrice(brick.Id, 1m, 0.95m);
        db.AddRange(requisition, retail);
        await db.SaveChangesAsync();

        (await scope.ServiceProvider.GetRequiredService<IQuotationService>().CreateAsync(companyId, customer.Id, "DL-Q1", new DateOnly(2026, 3, 4), new DateOnly(2026, 4, 3), Scenario.Usd,
            [new QuotationLineRequest(brick.Id, "Face brick", 400m, 0.9m, null, "4000")])).IsSuccess.Should().BeTrue();
        ISalesOrderService orders = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
        long so = (await orders.CreateOrderAsync(new CreateSalesOrderRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, OrderNumber = "DL-SO1", OrderDate = new DateOnly(2026, 3, 5), Currency = Scenario.Usd, RequestedDeliveryDate = new DateOnly(2026, 3, 9),
            Lines = [new SalesOrderLineRequest(brick.Id, "Face brick", 400m, 0.9m, null, "4000")],
        })).Value.SalesOrderId;
        long soLine = db.Set<One4All.Core.SalesOrders.SalesOrderLine>().Where(l => l.SalesOrderId == so).Select(l => l.Id).Single();
        (await orders.DespatchAsync(new DespatchRequest
        {
            CompanyId = companyId, SalesOrderId = so, WarehouseId = yard.Id, Date = new DateOnly(2026, 3, 8), Reference = "DL-D1", Lines = [new DespatchLine(soLine, 400m)], Vehicle = "TRK1", Driver = "Sam",
        })).IsSuccess.Should().BeTrue();

        IDocumentLayoutService documents = scope.ServiceProvider.GetRequiredService<IDocumentLayoutService>();
        string quote = (await documents.QuotationAsync(companyId, "DL-Q1")).Value;
        quote.Should().Contain("QUOTATION").And.Contain("To: Builder Ltd (DL-C)").And.Contain("Valid until: 2026-04-03").And.Contain("DL-BRK Face brick").And.Contain("360.00").And.Contain("Accepted by:");
        (await documents.OrderConfirmationAsync(companyId, "DL-SO1")).Value.Should().Contain("ORDER CONFIRMATION").And.Contain("Delivery wanted: 2026-03-09").And.Contain("360.00");
        string delivery = (await documents.DeliveryNoteAsync(companyId, "DL-D1")).Value;
        delivery.Should().Contain("DELIVERY NOTE").And.Contain("Deliver to: Builder Ltd").And.Contain("Vehicle: TRK1").And.Contain("DL-BRK Face brick").And.NotContain("0.90").And.Contain("Received by:");
        (await documents.DeliveryNoteAsync(companyId, "DL-D1", packing: true)).Value.Should().Contain("PACKING NOTE").And.Contain("Total weight: 1000 kg");
        (await documents.RequisitionAsync(companyId, "DL-RQ1")).Value.Should().Contain("PURCHASE REQUISITION").And.Contain("Face brick [6000]").And.Contain("275.00").And.Contain("Budget checked by:");
        (await documents.GoodsReceivedNoteAsync(companyId, "DL-GRN1")).Value.Should().Contain("GOODS RECEIVED NOTE").And.Contain("Purchase order: DL-PO1").And.Contain("500.00");
        (await documents.ShelfLabelsAsync(companyId, "RETAIL")).Value.Should().Contain("Face brick").And.Contain("USD 0.95").And.Contain("*6001234*");
        (await documents.QuotationAsync(companyId, "NOPE")).Error.Code.Should().Be("Doc.QuotationNotFound");

        // The designable reports give the same documents as rows for a company's own layout; a layout named for the kind prints through it as a PDF.
        One4All.Application.Abstractions.Reporting.IReportRunner runner = scope.ServiceProvider.GetRequiredService<One4All.Application.Abstractions.Reporting.IReportRunner>();
        async Task<string> Csv(string code, string number)
        {
            var rendered = await runner.RenderAsync(companyId, code, new Dictionary<string, object?> { ["Number"] = number }, One4All.Application.Abstractions.Reporting.ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return System.Text.Encoding.UTF8.GetString(rendered.Value.Content);
        }

        string quotation = await Csv("DOC01", "DL-Q1");
        quotation.Should().Contain("Quotation").And.Contain("Builder Ltd (DL-C)").And.Contain("DL-Q1").And.Contain("2026-04-03").And.Contain("DL-BRK").And.Contain("Face brick").And.Contain("360");
        quotation.Should().Contain("TOTAL (USD) excl. tax", "the last total and its label ride on every row");
        (await Csv("DOC03", "DL-D1")).Should().Contain("Delivery Note").And.Contain("Deliver to").And.Contain("DL-D1").And.Contain("DL-SO1").And.Contain("TRK1").And.Contain("Face brick");
        (await Csv("DOC12", "DL-PO1")).Should().Contain("Brick Co").And.Contain("DL-PO1").And.Contain("Face brick");
        (await Csv("DOC01", "NOPE")).Should().NotContain("DL-Q1");

        One4All.Application.Documents.DocumentLayoutSettings settings = One4All.Application.Documents.DocumentLayoutSettings.Parse(null);
        settings.Set(One4All.Application.Documents.DocumentKinds.Quotation, "doc01");
        settings = One4All.Application.Documents.DocumentLayoutSettings.Parse(settings.ToJson());
        (settings.LayoutFor(One4All.Application.Documents.DocumentKinds.Quotation), settings.LayoutFor(One4All.Application.Documents.DocumentKinds.DeliveryNote)).Should().Be(("DOC01", null));
        var pdf = await runner.RenderAsync(companyId, settings.LayoutFor(One4All.Application.Documents.DocumentKinds.Quotation)!, new Dictionary<string, object?> { ["Number"] = "DL-Q1" },
            One4All.Application.Abstractions.Reporting.ReportFormat.Pdf);
        pdf.IsSuccess.Should().BeTrue(pdf.IsFailure ? pdf.Error.Message : null);
        pdf.Value.Content.Take(4).Should().Equal("%PDF"u8.ToArray());
    }

    [SkippableFact]
    public async Task Debit_notes_bills_fee_invoices_and_payment_certificates_render()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.AddRange(new One4All.Core.Accounting.LedgerAccount(companyId, "5190", "Purchase Price Variance", One4All.Core.Accounting.AccountType.Expense),
            new One4All.Core.Accounting.LedgerAccount(companyId, "1215", "Retention Receivable", One4All.Core.Accounting.AccountType.Asset));
        Customer client = new(companyId, "DL2-C", "Council Client", Scenario.Usd), parent = new(companyId, "DL2-P", "Parent P", Scenario.Usd), payer = new(companyId, "DL2-R", "Rates Payer", Scenario.Usd);
        Supplier supplier = new(companyId, "DL2-S", "Brick Co", Scenario.Usd);
        Item brick = new(companyId, "DL2-BRK", "Face brick") { CostOfSalesAccountCode = "5000" };
        Warehouse yard = new(companyId, "DL2-WH", "Yard");
        One4All.Core.Municipal.Tariff refuse = new(companyId, "DL2-REF", "Refuse Removal", One4All.Core.Municipal.ServiceType.Refuse, One4All.Core.Municipal.TariffType.Flat, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 30m };
        db.AddRange(client, parent, payer, supplier, brick, yard, refuse);
        await db.SaveChangesAsync();
        One4All.Core.Municipal.Property stand = new(companyId, "DL2-901", "Ward 9", payer.Id);
        stand.AddService(One4All.Core.Municipal.ServiceType.Refuse, refuse.Id, new DateOnly(2026, 1, 1));
        One4All.Core.Schools.FeeStructure fees = new(companyId, "DL2-G3", "Grade 3 Term 2", "G3", "Term 2", "4200");
        fees.AddItem("Tuition", 1_000m, true);
        fees.AddItem("Levy", 200m, true);
        db.AddRange(stand, fees);
        await db.SaveChangesAsync();
        One4All.Core.Schools.Student kid = new(companyId, "DL2-STU", "Kid K", "G3", parent.Id);
        kid.GrantBursary(One4All.Core.Schools.BursaryType.Percentage, 10m);
        db.Students.Add(kid);
        await db.SaveChangesAsync();

        (await scope.ServiceProvider.GetRequiredService<IInventoryService>().ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = brick.Id, WarehouseId = yard.Id, Date = new DateOnly(2026, 4, 1), Quantity = 500m, UnitCost = 0.5m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();
        (await scope.ServiceProvider.GetRequiredService<IProcurementService>().ReturnToSupplierAsync(companyId, supplier.Id, brick.Id, yard.Id, 100m, 0.5m, new DateOnly(2026, 4, 3), "DL2-DN1", "5190"))
            .IsSuccess.Should().BeTrue();
        One4All.Application.Abstractions.Municipal.IBillingRunService runs = scope.ServiceProvider.GetRequiredService<One4All.Application.Abstractions.Municipal.IBillingRunService>();
        var proposed = (await runs.ProposeAsync(new One4All.Application.Abstractions.Municipal.BillingRunProposal
        {
            CompanyId = companyId, ServiceType = One4All.Core.Municipal.ServiceType.Refuse, BillDate = new DateOnly(2026, 4, 30), Ward = "Ward 9", InvoicePrefix = "DL2B", UserId = 71,
        })).Value;
        (await runs.ApproveAsync(companyId, proposed.RunNumber, 72)).IsSuccess.Should().BeTrue();
        (await scope.ServiceProvider.GetRequiredService<One4All.Application.Abstractions.Schools.ISchoolBillingService>().BillTermAsync(companyId, fees.Id, "DL2-F-", new DateOnly(2026, 4, 5))).IsSuccess.Should().BeTrue();
        One4All.Application.Abstractions.JobCosting.IJobCostingService jobs = scope.ServiceProvider.GetRequiredService<One4All.Application.Abstractions.JobCosting.IJobCostingService>();
        long contract = (await jobs.CreateContractAsync(companyId, "DL2-K1", "Civic hall", client.Id, 200_000m, 150_000m, 10m, "4300")).Value;
        (await jobs.CertifyProgressAsync(companyId, contract, "IPC-1", new DateOnly(2026, 4, 20), 50_000m, 5_000m, "1215")).IsSuccess.Should().BeTrue();

        IDocumentLayoutService documents = scope.ServiceProvider.GetRequiredService<IDocumentLayoutService>();
        (await documents.SupplierDebitNoteAsync(companyId, "DL2-DN1")).Value.Should().Contain("DEBIT NOTE").And.Contain("Supplier: Brick Co (DL2-S)").And.Contain("DL2-BRK Face brick").And.Contain("CREDIT CLAIMED (USD)")
            .And.Contain("50.00");
        string bill = (await documents.MunicipalBillAsync(companyId, "DL2-901", new DateOnly(2026, 4, 1))).Value;
        bill.Should().Contain("MUNICIPAL ACCOUNT").And.Contain("Ratepayer: Rates Payer (DL2-R)").And.Contain("Stand: DL2-901").And.Contain("Refuse").And.Contain("TOTAL DUE");
        string invoice = (await documents.FeeInvoiceAsync(companyId, "DL2-F-DL2-STU")).Value;
        invoice.Should().Contain("FEE INVOICE").And.Contain("Student: Kid K (DL2-STU)").And.Contain("Tuition").And.Contain("Levy").And.Contain("Less bursary / discount").And.Contain("1,080.00");
        string ipc = (await documents.PaymentCertificateAsync(companyId, "IPC-1")).Value;
        ipc.Should().Contain("INTERIM PAYMENT CERTIFICATE").And.Contain("Employer: Council Client (DL2-C)").And.Contain("Gross valuation").And.Contain("55,000.00").And.Contain("NET AMOUNT DUE").And.Contain("49,500.00");
        (await documents.MunicipalBillAsync(companyId, "DL2-901", new DateOnly(2026, 2, 1))).Error.Code.Should().Be("Doc.BillNotFound");
    }
}
