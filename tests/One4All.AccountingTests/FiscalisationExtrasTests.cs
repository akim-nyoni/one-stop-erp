using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers fiscalisation beyond sales (brief §3.5): a refund and a void are signed as refund receipts that reference the receipt they reverse, a sales
/// ledger tax invoice and credit note are e-invoiced with the buyer's tax number, a device that is down queues them for the retry task, and the day close
/// submits the Z report with the day's counts and totals.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class FiscalisationExtrasTests
{
    private readonly AccountingFixture _fixture;

    public FiscalisationExtrasTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Refunds_voids_invoices_and_the_Z_report_are_fiscalised()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        SimulatedFiscalDeviceProvider device = new();
        await using ServiceProvider provider = _fixture.BuildServiceProvider(s => s.AddSingleton<IFiscalDeviceProvider>(device));
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Branch branch = new(companyId, "FX", "Shop");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        PosTerminal terminal = new(companyId, branch.Id, "FXTILL", "Till");
        Warehouse shop = new(companyId, "FX-WH", "Shop floor");
        Item soap = new(companyId, "FX-SOAP", "Soap") { RevenueAccountCode = "4000" };
        Customer buyer = new(companyId, "FX-C", "Fiscal Buyer", Scenario.Usd) { TaxRegistration = "VAT-77" };
        db.AddRange(terminal, shop, soap, buyer);
        await db.SaveChangesAsync();

        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        IPosEventService events = scope.ServiceProvider.GetRequiredService<IPosEventService>();
        long session = (await pos.OpenTillAsync(companyId, terminal.Id, cashierUserId: 1, openingFloat: 0m)).Value;
        DateOnly day = new(2026, 6, 2);
        Task<Result<CompleteSaleResult>> Sell(string number) => pos.CompleteSaleAsync(new CompleteSaleRequest
        {
            CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = 1, SaleNumber = number, SaleDate = day, WarehouseId = shop.Id, TaxAccountCode = "2200",
            Lines = [new PosSaleLineRequest(soap.Id, "Soap", 2m, 5m, 10m, 1.5m, "4000")],
            Tenders = [new PosTenderRequest(TenderType.Cash, Scenario.Usd, 11.5m, "1000", IsChange: false)],
        });
        CompleteSaleResult first = (await Sell("FX-1")).Value;
        CompleteSaleResult second = (await Sell("FX-2")).Value;

        // A refund is a signed refund receipt referencing the sale; so is a void.
        Sale sold = await db.Set<Sale>().AsNoTracking().Include(s => s.Lines).SingleAsync(s => s.Id == first.SaleId);
        var refunded = await events.RefundAsync(new PosRefundRequest
        {
            CompanyId = companyId, SaleId = first.SaleId, TerminalId = terminal.Id, CashierUserId = 1, AuthorisedByUserId = 2, Date = day, RefundAccountCode = "1000", TaxAccountCode = "2200",
            Reason = "Damaged", Lines = [new PosRefundLine(sold.Lines[0].Id, 1m)], TillSessionId = session,
        });
        refunded.IsSuccess.Should().BeTrue(refunded.IsFailure ? refunded.Error.Message : null);
        PosEvent refundEvent = await db.PosEvents.AsNoTracking().SingleAsync(e => e.CompanyId == companyId && e.Kind == PosEventKind.Refund);
        (refundEvent.FiscalQueued, refundEvent.FiscalCode).Should().Be((false, "SIM-0001-000003"));
        refundEvent.FiscalVerification.Should().StartWith("https://verify.example/");
        device.Signed.Should().Contain(r => r.Kind == FiscalDocumentKind.Refund && r.DocumentNumber == refunded.Value.JournalNumber && r.ReferenceDocumentNumber == "FX-1" && r.TotalGross == 5.75m);

        (await pos.VoidSaleAsync(companyId, second.SaleId, day, "Keyed twice", "2200", authorisedByUserId: 2)).IsSuccess.Should().BeTrue();
        PosEvent voidEvent = await db.PosEvents.AsNoTracking().SingleAsync(e => e.CompanyId == companyId && e.Kind == PosEventKind.SaleVoid);
        (voidEvent.Reason, voidEvent.AuthorisedByUserId).Should().Be(("Keyed twice", 2L), "the void keeps its reason and the supervisor who authorised it (thirteenth audit #4)");
        (voidEvent.FiscalQueued, voidEvent.FiscalCode).Should().Be((false, "SIM-0001-000004"));
        device.Signed.Should().Contain(r => r.Kind == FiscalDocumentKind.Refund && r.ReferenceDocumentNumber == "FX-2" && r.TotalGross == 11.5m);

        // E-invoicing: a tax invoice and a credit note from the sales ledger, with the buyer's tax number; the device down queues them and the retry signs them.
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        var invoice = await ar.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = buyer.Id, DocumentNumber = "FX-INV1", DocumentDate = day, Currency = Scenario.Usd, NetAmount = 100m, TaxAmount = 15m, TaxAccountCode = "2200",
            TaxCode = "S", SalesAccountCode = "4000", Narrative = "Consulting",
        });
        invoice.IsSuccess.Should().BeTrue(invoice.IsFailure ? invoice.Error.Message : null);
        OpenItem signedInvoice = await db.OpenItems.AsNoTracking().SingleAsync(i => i.Id == invoice.Value.OpenItemId);
        (signedInvoice.FiscalQueued, signedInvoice.FiscalCode).Should().Be((false, "SIM-0001-000005"));
        device.Signed.Should().Contain(r => r.Kind == FiscalDocumentKind.Invoice && r.DocumentNumber == "FX-INV1" && r.TotalGross == 115m && r.TotalTax == 15m && r.CustomerTaxNumber == "VAT-77");

        device.Offline = true;
        var credit = await ar.PostCreditNoteAsync(new SalesCreditNoteRequest
        {
            CompanyId = companyId, CustomerId = buyer.Id, DocumentNumber = "FX-CRN1", DocumentDate = day, Currency = Scenario.Usd, NetAmount = 20m, TaxAmount = 3m, TaxAccountCode = "2200",
            SalesAccountCode = "4000", Narrative = "Part credit",
        });
        credit.IsSuccess.Should().BeTrue(credit.IsFailure ? credit.Error.Message : null);
        (await db.OpenItems.AsNoTracking().SingleAsync(i => i.Id == credit.Value.OpenItemId)).FiscalQueued.Should().BeTrue("the device was down; the credit note waits");
        (await pos.RetryFiscalAsync(companyId)).Should().Be((0, 1));
        device.Offline = false;
        (await pos.RetryFiscalAsync(companyId)).Should().Be((1, 0));
        OpenItem signedCredit = await db.OpenItems.AsNoTracking().SingleAsync(i => i.Id == credit.Value.OpenItemId);
        (signedCredit.FiscalQueued, signedCredit.FiscalCode).Should().Be((false, "SIM-0001-000006"));
        device.Signed.Should().Contain(r => r.Kind == FiscalDocumentKind.CreditNote && r.DocumentNumber == "FX-CRN1");

        // The day close submits the Z report: one sale standing (FX-1, less its refund shown separately), one refund, one void.
        (await pos.CashUpAndCloseAsync(new CashUpRequest { TillSessionId = session, CountedCash = 11.5m, Date = day, CashAccountCode = "1000", OverShortAccountCode = "6900" })).IsSuccess.Should().BeTrue();
        device.LastDayReport.Should().NotBeNull();
        FiscalDayReport report = device.LastDayReport!.Value;
        (report.Date, report.SaleCount, report.SalesGross, report.SalesTax, report.RefundCount, report.RefundsGross, report.VoidCount, report.VoidsGross, report.Currency)
            .Should().Be((day, 1, 11.5m, 1.5m, 1, 5.75m, 1, 11.5m, "USD"));
    }
}
