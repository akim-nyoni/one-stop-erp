using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class TradingContraTests
{
    private readonly AccountingFixture _fixture;

    public TradingContraTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Contra_offsets_a_debtor_invoice_against_a_creditor_invoice_for_the_same_partner()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        long supplierId;
        long arItemId;
        long apItemId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            // The same partner is both a customer and a supplier.
            Customer customer = new(companyId, "PARTNER", "Trading Partner (AR)", Scenario.Usd);
            db.Customers.Add(customer);
            Supplier supplier = new(companyId, "PARTNER", "Trading Partner (AP)", Scenario.Usd);
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            customerId = customer.Id;
            supplierId = supplier.Id;

            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            arItemId = (await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = customerId, DocumentNumber = "CN-AR1", DocumentDate = new DateOnly(2026, 3, 1),
                Currency = Scenario.Usd, NetAmount = 300m, TaxAmount = 0m, SalesAccountCode = "4000",
            })).Value.OpenItemId;

            IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
            apItemId = (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest
            {
                CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "CN-AP1", DocumentDate = new DateOnly(2026, 3, 2),
                Currency = Scenario.Usd, NetAmount = 200m, ExpenseAccountCode = "5000",
            })).Value.OpenItemId;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IAllocationService alloc = scope.ServiceProvider.GetRequiredService<IAllocationService>();

            // Cannot contra more than the smaller balance (AP is 200).
            (await alloc.ContraAsync(companyId, customerId, arItemId, supplierId, apItemId, 250m, new DateOnly(2026, 3, 5), "1900", "CONTRA-1")).IsFailure.Should().BeTrue();

            // Offset 200: clears the AP invoice fully and reduces the AR invoice to 100.
            (await alloc.ContraAsync(companyId, customerId, arItemId, supplierId, apItemId, 200m, new DateOnly(2026, 3, 5), "1900", "CONTRA-1")).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.OpenItems.FirstAsync(i => i.Id == arItemId)).OutstandingTransaction.Should().Be(100m); // 300 − 200
            (await db.OpenItems.FirstAsync(i => i.Id == apItemId)).OutstandingTransaction.Should().Be(0m);   // 200 − 200

            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            (await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 3, 31))).Rows.Should().Contain(r => r.TraderCode == "PARTNER" && r.Total == 100m);
            (await reports.AgedCreditorsAsync(companyId, new DateOnly(2026, 3, 31))).Rows.Should().NotContain(r => r.TraderCode == "PARTNER");

            Application.Abstractions.Reporting.IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<Application.Abstractions.Reporting.IGeneralLedgerReports>();
            var tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            // The clearing account nets to zero (200 in from AR, 200 out to AP).
            tb.Rows.Should().NotContain(r => r.AccountCode == "1900" && (r.Debit != 0m || r.Credit != 0m));
        }
    }

    [SkippableFact]
    public async Task Contra_refuses_items_in_different_currencies()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer customer = new(companyId, "XPARTNER", "Euro Partner (AR)", Scenario.Eur);
        Supplier supplier = new(companyId, "XPARTNER", "Dollar Partner (AP)", Scenario.Usd);
        db.AddRange(customer, supplier);
        await db.SaveChangesAsync();

        var arInvoice = await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "XCN-AR1", DocumentDate = new DateOnly(2026, 3, 1),
            Currency = Scenario.Eur, NetAmount = 300m, TaxAmount = 0m, SalesAccountCode = "4000",
        });
        arInvoice.IsSuccess.Should().BeTrue(arInvoice.IsFailure ? arInvoice.Error.Message : null);
        long apItemId = (await scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>().PostInvoiceAsync(new PurchaseInvoiceRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = "XCN-AP1", DocumentDate = new DateOnly(2026, 3, 2),
            Currency = Scenario.Usd, NetAmount = 200m, ExpenseAccountCode = "5000",
        })).Value.OpenItemId;

        // A EUR receipt and a USD payment of the same 100 would leave the clearing account out by the exchange rate: refused, nothing posted.
        var contra = await scope.ServiceProvider.GetRequiredService<IAllocationService>().ContraAsync(companyId, customer.Id, arInvoice.Value.OpenItemId, supplier.Id, apItemId, 100m,
            new DateOnly(2026, 3, 5), "1900", "XCONTRA-1");
        contra.Error.Code.Should().Be("Trd.ContraCurrency");
        (await db.OpenItems.AsNoTracking().AnyAsync(i => i.CompanyId == companyId && i.DocumentNumber == "XCONTRA-1")).Should().BeFalse();
        (await db.OpenItems.AsNoTracking().SingleAsync(i => i.Id == apItemId)).OutstandingTransaction.Should().Be(200m);
    }
}
