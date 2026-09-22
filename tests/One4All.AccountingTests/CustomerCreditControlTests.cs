using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class CustomerCreditControlTests
{
    private readonly AccountingFixture _fixture;

    public CustomerCreditControlTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Invoices_are_refused_over_the_credit_limit_or_when_the_customer_is_on_hold()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long limitedId;
        long heldId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer limited = new(companyId, "CC-LIM", "Limited Buyer", Scenario.Usd) { CreditLimit = 1000m };
            Customer held = new(companyId, "CC-HOLD", "Blocked Buyer", Scenario.Usd) { OnHold = true };
            db.Customers.AddRange(limited, held);
            await db.SaveChangesAsync();
            limitedId = limited.Id;
            heldId = held.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();

            // On-hold customer: any invoice is refused.
            (await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = heldId, DocumentNumber = "CC-H1", DocumentDate = new DateOnly(2026, 4, 30),
                Currency = Scenario.Usd, NetAmount = 10m, TaxAmount = 0m, SalesAccountCode = "4000",
            })).IsFailure.Should().BeTrue();

            // Within the 1000 limit: 600 invoice posts.
            (await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = limitedId, DocumentNumber = "CC-L1", DocumentDate = new DateOnly(2026, 4, 30),
                Currency = Scenario.Usd, NetAmount = 600m, TaxAmount = 0m, SalesAccountCode = "4000",
            })).IsSuccess.Should().BeTrue();

            // A further 500 would take the balance to 1100 (> 1000) → refused.
            (await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = limitedId, DocumentNumber = "CC-L2", DocumentDate = new DateOnly(2026, 4, 30),
                Currency = Scenario.Usd, NetAmount = 500m, TaxAmount = 0m, SalesAccountCode = "4000",
            })).IsFailure.Should().BeTrue();

            // A further 400 (→ 1000, exactly the limit) is allowed.
            (await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = limitedId, DocumentNumber = "CC-L3", DocumentDate = new DateOnly(2026, 4, 30),
                Currency = Scenario.Usd, NetAmount = 400m, TaxAmount = 0m, SalesAccountCode = "4000",
            })).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            AgeingReport debtors = await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 4, 30));
            debtors.Rows.Should().Contain(r => r.TraderCode == "CC-LIM" && r.Total == 1000m); // 600 + 400 posted
            debtors.Rows.Should().NotContain(r => r.TraderCode == "CC-HOLD");                  // nothing posted
        }
    }
}
