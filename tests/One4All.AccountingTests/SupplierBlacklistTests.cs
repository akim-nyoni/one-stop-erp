using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the supplier blacklist: once blacklisted, a supplier cannot be given an order, named on a requisition or
/// invited to quote, but its existing invoice is still paid; an order for an unknown supplier is refused; after
/// reinstatement an order goes through.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SupplierBlacklistTests
{
    private readonly AccountingFixture _fixture;

    public SupplierBlacklistTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Blacklisted_suppliers_get_no_new_business_but_are_still_paid()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Supplier banned = new(companyId, "BL-SUP", "Fraudulent Ltd", Scenario.Usd);
        Supplier fine = new(companyId, "BL-OK", "Honest Ltd", Scenario.Usd);
        Item widget = new(companyId, "BL-W", "Widget");
        db.AddRange(banned, fine, widget);
        await db.SaveChangesAsync();

        IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
        (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest
        {
            CompanyId = companyId, SupplierId = banned.Id, DocumentNumber = "BL-I1", DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, NetAmount = 100m, ExpenseAccountCode = "6800",
        })).IsSuccess.Should().BeTrue();

        banned.Blacklist("Invoiced for goods never delivered");
        await db.SaveChangesAsync();

        IProcurementService pop = scope.ServiceProvider.GetRequiredService<IProcurementService>();
        CreatePurchaseOrderRequest Order(long supplierId, string number) => new()
        {
            CompanyId = companyId, SupplierId = supplierId, OrderNumber = number, OrderDate = new DateOnly(2026, 3, 5), Currency = Scenario.Usd,
            Lines = [new PurchaseOrderLineRequest(widget.Id, "Widget", 1m, 10m)],
        };
        (await pop.CreatePurchaseOrderAsync(Order(banned.Id, "BL-PO1"))).Error.Code.Should().Be("Pop.SupplierBlacklisted");
        (await pop.CreatePurchaseOrderAsync(Order(999_999, "BL-PO2"))).Error.Code.Should().Be("Pop.SupplierNotFound");
        (await scope.ServiceProvider.GetRequiredService<IPurchaseRequisitionService>().CreateAsync(companyId, banned.Id, "BL-REQ", new DateOnly(2026, 3, 5), Scenario.Usd,
            [new PurchaseRequisitionLineRequest(widget.Id, "Widget", 1m, 10m)])).Error.Code.Should().Be("Pop.SupplierBlacklisted");
        (await scope.ServiceProvider.GetRequiredService<IRfqService>().IssueAsync(companyId, new RfqRequest("BL-RFQ", new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 10),
            [banned.Id, fine.Id], [new RfqLineRequest(widget.Id, "Widget", 1m)]))).Error.Code.Should().Be("Rfq.Supplier");

        (await ap.PostPaymentAsync(new PaymentRequest
        {
            CompanyId = companyId, SupplierId = banned.Id, DocumentNumber = "BL-P1", PaymentDate = new DateOnly(2026, 3, 20), Currency = Scenario.Usd, Amount = 100m, BankAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();

        banned.Reinstate();
        await db.SaveChangesAsync();
        (await pop.CreatePurchaseOrderAsync(Order(banned.Id, "BL-PO3"))).IsSuccess.Should().BeTrue();
    }
}
