using FluentAssertions;
using One4All.Application.Abstractions.MasterData;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Api;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using Xunit;

namespace One4All.UnitTests.Api;

/// <summary>The REST API's documents map onto the application's requests: currencies and enums parsed, the company stamped on, bad input refused.</summary>
public sealed class TradingApiMappingTests
{
    [Fact]
    public void Sales_documents_map_with_their_currency_and_analysis()
    {
        SalesDocumentApi api = new(7, "INV-1", new DateOnly(2026, 9, 1), "usd", 100m, "4000", 15m, "2200", "VAT", null, "Goods", "ADM", "SALES", "RET");
        SalesInvoiceRequest invoice = TradingApiMapping.SalesInvoice(3, api).Value;
        invoice.Should().BeEquivalentTo(new { CompanyId = 3L, CustomerId = 7L, DocumentNumber = "INV-1", NetAmount = 100m, TaxAmount = 15m, SalesAccountCode = "4000", TaxAccountCode = "2200", TaxCode = "VAT", CostCentre = "ADM", Department = "SALES" });
        invoice.Currency.Value.Should().Be("USD");
        SalesCreditNoteRequest credit = TradingApiMapping.SalesCreditNote(3, api).Value;
        credit.ReasonCode.Should().Be("RET");
        credit.Currency.Value.Should().Be("USD");

        TradingApiMapping.SalesInvoice(3, api with { Currency = "US DOLLARS" }).Error.Code.Should().Be("Api.Currency");
    }

    [Fact]
    public void Purchase_documents_receipts_and_payments_map()
    {
        PurchaseDocumentApi purchase = new(9, "S-INV-5", new DateOnly(2026, 9, 2), "eur", 200m, "6800", TaxCode: "PVAT");
        PurchaseInvoiceRequest invoice = TradingApiMapping.PurchaseInvoice(3, purchase).Value;
        invoice.Should().BeEquivalentTo(new { CompanyId = 3L, SupplierId = 9L, ExpenseAccountCode = "6800", TaxCode = "PVAT", TaxAmount = 0m });
        invoice.Currency.Value.Should().Be("EUR");
        TradingApiMapping.PurchaseCreditNote(3, purchase).Value.DocumentNumber.Should().Be("S-INV-5");

        ReceiptRequest receipt = TradingApiMapping.Receipt(3, new ReceiptApi(7, "RC-1", new DateOnly(2026, 9, 3), "USD", 50m, "1100")).Value;
        receipt.Should().BeEquivalentTo(new { CompanyId = 3L, CustomerId = 7L, Amount = 50m, BankAccountCode = "1100" });

        PaymentRequest payment = TradingApiMapping.SupplierPayment(3, new SupplierPaymentApi(9, "PAY-1", new DateOnly(2026, 9, 4), "USD", 80m, "1100", WithholdingTaxCode: "WHT")).Value;
        payment.Should().BeEquivalentTo(new { SupplierId = 9L, Amount = 80m, WithholdingTaxCode = "WHT" });
    }

    [Fact]
    public void Till_sales_map_lines_and_tenders_and_refuse_bad_tender_types()
    {
        PosSaleApi sale = new(1, 2, 3, "S-9", new DateOnly(2026, 9, 5), 4,
            [new PosSaleLineApi(10, "Soda", 2m, 5m, 10m, 1.5m, "4000", Note: "cold", SerialNumber: null, GiftWrap: false)],
            [new PosTenderApi("cash", "USD", 11.5m, "1000")], "2200");
        CompleteSaleRequest request = TradingApiMapping.PosSale(3, sale).Value;
        request.Should().BeEquivalentTo(new { CompanyId = 3L, TerminalId = 1L, TillSessionId = 2L, CashierUserId = 3L, SaleNumber = "S-9", WarehouseId = 4L, TaxAccountCode = "2200" });
        request.Lines.Should().ContainSingle(l => l.ItemId == 10 && l.Quantity == 2m && l.Note == "cold");
        request.Tenders.Should().ContainSingle(t => t.TenderType == TenderType.Cash && t.Amount == 11.5m && t.Currency.Value == "USD");

        TradingApiMapping.PosSale(3, sale with { Tenders = [new PosTenderApi("beads", "USD", 1m, "1000")] }).Error.Code.Should().Be("Api.TenderType");
        TradingApiMapping.PosSale(3, sale with { Lines = [] }).Error.Code.Should().Be("Api.Sale");
    }

    [Fact]
    public void An_item_change_goes_through_the_master_data_update_keeping_what_it_leaves_out()
    {
        Item item = new(3, "BOLT", "Bolt", ItemType.Stock, CostingMethod.Standard)
        {
            StandardCost = 2m, RevenueAccountCode = "4000", CostOfSalesAccountCode = "5000", PriceVarianceAccountCode = "6000", UsageVarianceAccountCode = "6100", Category = "HARDWARE",
        };

        ItemUpdate kept = TradingApiMapping.ItemChange(item, new ItemApi("ignored", " "));
        kept.Should().Be(new ItemUpdate("BOLT", "Bolt", "4000", "5000", 2m, "6000", "6100", "HARDWARE"), "what the request leaves out keeps the item's value");

        ItemUpdate changed = TradingApiMapping.ItemChange(item, new ItemApi("ignored", " Bolt M8 ", RevenueAccountCode: " 4100 ", Category: "fixings", StandardCost: 3m,
            PriceVarianceAccountCode: "6050"));
        changed.Should().Be(new ItemUpdate("BOLT", "Bolt M8", "4100", "5000", 3m, "6050", "6100", "FIXINGS"),
            "the standard cost reaches the service, whose revaluation rule then decides whether it may change");
    }
}
