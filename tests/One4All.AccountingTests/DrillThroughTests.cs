using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Reporting.Engine;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Drill-through from report cells: documents, stands and tax codes render as text; field names imply their targets.</summary>
[Collection(AccountingCollection.Name)]
public sealed class DrillThroughTests
{
    private readonly AccountingFixture _fixture;

    public DrillThroughTests(AccountingFixture fixture) => _fixture = fixture;

    [Fact]
    public void Field_names_imply_drill_targets()
    {
        DrillThrough.Infer("InvoiceNumber").Should().Be("Document");
        DrillThrough.Infer("Document").Should().Be("Document");
        DrillThrough.Infer("Order").Should().Be("PurchaseOrder");
        DrillThrough.Infer("Grn").Should().Be("Grn");
        DrillThrough.Infer("Receipt").Should().Be("Receipt");
        DrillThrough.Infer("Stand").Should().Be("Stand");
        DrillThrough.Infer("TaxCode").Should().Be("TaxCode");
        DrillThrough.Infer("Payslip").Should().Be("Payslip");
        DrillThrough.Infer("Narrative").Should().BeNull();
    }

    [SkippableFact]
    public async Task Documents_stands_and_tax_codes_drill_to_text()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            TaxCode vat = new(companyId, "DRVAT", "VAT 15%", TaxType.Standard) { OutputAccountCode = "2200", InputAccountCode = "2210" };
            vat.AddRate(new DateOnly(2020, 1, 1), 15m);
            db.TaxCodes.Add(vat);
            Customer customer = new(companyId, "DR-C", "Drill Customer", Scenario.Usd);
            db.Customers.Add(customer);
            LandParcel parcel = new(companyId, $"LP-DR-{companyId}", "Drill parcel", 5000m, 50000m);
            db.LandParcels.Add(parcel);
            await db.SaveChangesAsync();
            HousingStand stand = new(companyId, parcel.Id, null, "DR-ST-1", 300m, 1000m);
            db.HousingStands.Add(stand);
            await db.SaveChangesAsync();
            stand.Sell(customer.Id, customer.Name, 4500m, new DateOnly(2026, 2, 1));
            await db.SaveChangesAsync();
            customerId = customer.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            DateOnly today = DateOnly.FromDateTime(DateTime.Today);
            var invoice = await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = customerId, DocumentNumber = "DR-INV-1", DocumentDate = today, Currency = Scenario.Usd, NetAmount = 200m, TaxAmount = 30m, SalesAccountCode = "4000", TaxAccountCode = "2200", TaxCode = "DRVAT" });
            invoice.IsSuccess.Should().BeTrue(invoice.IsFailure ? invoice.Error.ToString() : null);
            (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = customerId, DocumentNumber = "DR-RC-1", ReceiptDate = today, Currency = Scenario.Usd, Amount = 100m, BankAccountCode = "1100" })).IsSuccess.Should().BeTrue();
            IAllocationService allocations = scope.ServiceProvider.GetRequiredService<IAllocationService>();
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            long receiptId = db.OpenItems.Single(i => i.CompanyId == companyId && i.DocumentNumber == "DR-RC-1").Id;
            (await allocations.AllocateAsync(companyId, TradingLedger.AccountsReceivable, invoice.Value.OpenItemId, receiptId, 100m, today)).IsSuccess.Should().BeTrue();

            IDrillThroughService drill = scope.ServiceProvider.GetRequiredService<IDrillThroughService>();
            string document = (await drill.DescribeAsync(companyId, "Document", "DR-INV-1")).Value;
            document.Should().Contain("DR-INV-1").And.Contain("Drill Customer").And.Contain("230.00");
            string receipt = (await drill.DescribeAsync(companyId, "Receipt", "DR-RC-1")).Value;
            receipt.Should().Contain("Receipt DR-RC-1").And.Contain("Allocations").And.Contain("Invoice DR-INV-1").And.Contain("100.00");
            string stand = (await drill.DescribeAsync(companyId, "Stand", "DR-ST-1")).Value;
            stand.Should().Contain("Stand DR-ST-1").And.Contain("Sold").And.Contain("DR-C  Drill Customer").And.Contain("Sale").And.Contain("4,500.00");
            string tax = (await drill.DescribeAsync(companyId, "TaxCode", "drvat")).Value;
            tax.Should().Contain("Tax code DRVAT").And.Contain("DR-INV-1").And.Contain("30.00");
            (await drill.DescribeAsync(companyId, "Document", "NO-SUCH")).Error.Code.Should().Be("Drill.NotFound");
            (await drill.DescribeAsync(companyId, "Spaceship", "X")).Error.Code.Should().Be("Drill.NotFound");
        }
    }
}
