using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Trading;
using One4All.Core.Communications;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;
using IAllocationService = One4All.Application.Abstractions.Trading.IAllocationService;

namespace One4All.AccountingTests;

/// <summary>
/// Covers customer and supplier addresses and contacts: the default postal address prints on the invoice, statement, reminder letter,
/// purchase order and remittance advice; a new default replaces the old one; documents are emailed to the contacts who receive them
/// (else the primary contact, else the customer's own email), with the document attached; a trader with nobody to send to is refused.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class TraderDirectoryTests
{
    private readonly AccountingFixture _fixture;

    public TraderDirectoryTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Addresses_print_and_documents_go_to_the_right_people()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer customer = new(companyId, "TD-CUST", "Harare Hardware", Scenario.Usd) { Email = "accounts@harare-hw.example" };
        Supplier supplier = new(companyId, "TD-SUPP", "Bulawayo Cement", Scenario.Usd);
        Supplier silent = new(companyId, "TD-NONE", "No Contacts Ltd", Scenario.Usd);
        db.AddRange(customer, supplier, silent);
        await db.SaveChangesAsync();

        ITraderDirectoryService directory = scope.ServiceProvider.GetRequiredService<ITraderDirectoryService>();
        (await directory.AddAddressAsync(companyId, TradingLedger.AccountsReceivable, "td-cust", new NewTraderAddress(AddressKind.Physical, "12 Samora Machel Ave", City: "Harare"))).IsSuccess.Should().BeTrue();
        (await directory.AddAddressAsync(companyId, TradingLedger.AccountsReceivable, "TD-CUST", new NewTraderAddress(AddressKind.Postal, "P O Box 100", City: "Harare"))).IsSuccess.Should().BeTrue();
        long newer = (await directory.AddAddressAsync(companyId, TradingLedger.AccountsReceivable, "TD-CUST",
            new NewTraderAddress(AddressKind.Postal, "P O Box 2020", "Causeway", "Harare", null, "00263", "Zimbabwe"))).Value;
        (await directory.AddAddressAsync(companyId, TradingLedger.AccountsPayable, "TD-SUPP", new NewTraderAddress(AddressKind.Postal, "Private Bag 7", City: "Bulawayo"))).IsSuccess.Should().BeTrue();
        (await directory.AddAddressAsync(companyId, TradingLedger.AccountsReceivable, "NOPE", new NewTraderAddress(AddressKind.Postal, "x"))).Error.Code.Should().Be("Trader.NotFound");
        (await directory.AddAddressAsync(companyId, TradingLedger.AccountsReceivable, "TD-CUST", new NewTraderAddress(AddressKind.Postal, " "))).Error.Code.Should().Be("Trader.AddressLine");
        TraderDirectoryEntry entry = (await directory.DirectoryAsync(companyId, TradingLedger.AccountsReceivable, "TD-CUST")).Value;
        entry.Addresses.Where(a => a.Kind == AddressKind.Postal).Should().ContainSingle(a => a.IsDefault).Which.Id.Should().Be(newer);

        // The default postal address prints on the documents.
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        long invoice = (await ar.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "TD-INV1", DocumentDate = new DateOnly(2026, 2, 1), Currency = Scenario.Usd, NetAmount = 500m, SalesAccountCode = "4000",
        })).Value.OpenItemId;
        string invoiceText = (await scope.ServiceProvider.GetRequiredService<IInvoiceDocumentService>().RenderTextAsync(companyId, invoice)).Value;
        invoiceText.Should().Contain("P O Box 2020").And.Contain("Causeway").And.Contain("Harare 00263").And.Contain("Zimbabwe").And.NotContain("Samora Machel");
        (await scope.ServiceProvider.GetRequiredService<IStatementService>().RenderCustomerStatementTextAsync(companyId, customer.Id, new DateOnly(2026, 6, 30))).Value.Should().Contain("P O Box 2020");
        (await scope.ServiceProvider.GetRequiredService<IDunningService>().RenderReminderTextAsync(companyId, customer.Id, new DateOnly(2026, 6, 30), DunningStages.Default)).Value.Should().Contain("P O Box 2020");

        PurchaseOrder order = new(companyId, supplier.Id, $"TD-PO-{Guid.NewGuid():N}"[..14], new DateOnly(2026, 3, 1), Scenario.Usd);
        Item cement = new(companyId, "TD-CEM", "Cement 50kg");
        db.Items.Add(cement);
        await db.SaveChangesAsync();
        order.AddLine(cement.Id, "Cement 50kg", 100m, 9.5m);
        db.PurchaseOrders.Add(order);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IPurchaseOrderDocumentService>().RenderTextAsync(companyId, order.Id)).Value.Should().Contain("Private Bag 7").And.Contain("Bulawayo");

        IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
        long bill = (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = "TD-B1", DocumentDate = new DateOnly(2026, 3, 5), Currency = Scenario.Usd, NetAmount = 950m, ExpenseAccountCode = "5000" })).Value.OpenItemId;
        long payment = (await ap.PostPaymentAsync(new PaymentRequest { CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = "TD-PAY1", PaymentDate = new DateOnly(2026, 3, 20), Currency = Scenario.Usd, Amount = 950m, BankAccountCode = "1100" })).Value.OpenItemId;
        (await scope.ServiceProvider.GetRequiredService<IAllocationService>().AllocateAsync(companyId, TradingLedger.AccountsPayable, payment, bill, 950m, new DateOnly(2026, 3, 20))).IsSuccess.Should().BeTrue();
        (await scope.ServiceProvider.GetRequiredService<IRemittanceService>().RenderRemittanceTextAsync(companyId, payment)).Value.Should().Contain("Private Bag 7");

        // Email: the customer has no contacts, so its own email; the supplier's remittance goes to the remittance contact, the order to the buyer.
        async Task<List<OutboundMessage>> Batch(long id) => await db.Set<OutboundMessage>().AsNoTracking().Where(m => m.BatchId == id).ToListAsync();
        long statementBatch = (await directory.EmailStatementAsync(companyId, TradingLedger.AccountsReceivable, "TD-CUST", new DateOnly(2026, 6, 30))).Value;
        OutboundMessage statementMail = (await Batch(statementBatch)).Single();
        (statementMail.Recipient, statementMail.AttachmentName).Should().Be(("accounts@harare-hw.example", "statement-td-cust-20260630.txt"));
        Encoding.UTF8.GetString(statementMail.AttachmentContent!).Should().Contain("P O Box 2020");

        (await directory.AddContactAsync(companyId, TradingLedger.AccountsPayable, "TD-SUPP", new NewTraderContact("Rudo", "Accounts", "not-an-email"))).Error.Code.Should().Be("Trader.ContactEmail");
        (await directory.AddContactAsync(companyId, TradingLedger.AccountsPayable, "TD-SUPP", new NewTraderContact("Rudo", "Accounts", "rudo@bcement.example", ReceivesRemittances: true))).IsSuccess.Should().BeTrue();
        long buyer = (await directory.AddContactAsync(companyId, TradingLedger.AccountsPayable, "TD-SUPP", new NewTraderContact("Tendai", "Sales", "tendai@bcement.example", IsPrimary: true))).Value;
        (await Batch((await directory.EmailRemittanceAsync(companyId, payment)).Value)).Select(m => m.Recipient).Should().Equal("rudo@bcement.example");
        (await Batch((await directory.EmailPurchaseOrderAsync(companyId, order.Id)).Value)).Select(m => m.Recipient).Should().Equal("tendai@bcement.example");
        (await directory.RemoveContactAsync(companyId, buyer)).IsSuccess.Should().BeTrue();
        (await directory.EmailPurchaseOrderAsync(companyId, order.Id)).Error.Code.Should().Be("Trader.NoRecipient");
        (await directory.EmailStatementAsync(companyId, TradingLedger.AccountsPayable, "TD-NONE", new DateOnly(2026, 6, 30))).Error.Code.Should().Be("Trader.NoRecipient");
    }
}
