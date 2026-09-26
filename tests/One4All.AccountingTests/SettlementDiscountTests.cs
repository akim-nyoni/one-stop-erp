using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class SettlementDiscountTests
{
    private readonly AccountingFixture _fixture;

    public SettlementDiscountTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Settling_with_an_early_payment_discount_clears_the_invoice()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        long invoiceItemId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "SD-CUST", "Prompt Payer", Scenario.Usd);
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;

            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            invoiceItemId = (await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = customerId, DocumentNumber = "SD-1", DocumentDate = new DateOnly(2026, 3, 1),
                Currency = Scenario.Usd, NetAmount = 1_000m, TaxAmount = 0m, SalesAccountCode = "4000",
            })).Value.OpenItemId;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();

            // Cash + discount cannot exceed the outstanding 1,000.
            (await ar.SettleWithDiscountAsync(companyId, invoiceItemId, new DateOnly(2026, 3, 5), 990m, 20m, "1100", "4700", "SD-R0")).IsFailure.Should().BeTrue();

            // Pay 980 now, take a 20 early-payment discount.
            var settle = await ar.SettleWithDiscountAsync(companyId, invoiceItemId, new DateOnly(2026, 3, 5), 980m, 20m, "1100", "4700", "SD-R1");
            settle.IsSuccess.Should().BeTrue(settle.IsFailure ? settle.Error.ToString() : null);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            OpenItem invoice = await db.OpenItems.FirstAsync(i => i.Id == invoiceItemId);
            invoice.OutstandingTransaction.Should().Be(0m); // fully settled by cash + discount

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "1100" && r.Debit == 980m);  // cash banked
            tb.Rows.Should().Contain(r => r.AccountCode == "4700" && r.Debit == 20m);   // discount allowed

            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            AgeingReport debtors = await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 3, 31));
            debtors.Rows.Should().NotContain(r => r.TraderCode == "SD-CUST");
        }
    }
}
