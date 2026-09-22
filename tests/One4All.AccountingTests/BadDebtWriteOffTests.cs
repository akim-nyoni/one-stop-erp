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
public sealed class BadDebtWriteOffTests
{
    private readonly AccountingFixture _fixture;

    public BadDebtWriteOffTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Writing_off_a_bad_debt_clears_the_debtor_and_charges_bad_debt_expense()
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
            Customer customer = new(companyId, "BD-C", "Deadbeat Buyer", Scenario.Usd) { PaymentTermsDays = 30 };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;

            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            var invoice = await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = customerId, DocumentNumber = "BD-INV1", DocumentDate = new DateOnly(2026, 1, 5),
                Currency = Scenario.Usd, NetAmount = 300m, TaxAmount = 0m, SalesAccountCode = "4000",
            });
            invoice.IsSuccess.Should().BeTrue(invoice.IsFailure ? invoice.Error.ToString() : null);
            invoiceItemId = invoice.Value.OpenItemId;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            (await ar.WriteOffAsync(companyId, invoiceItemId, new DateOnly(2026, 4, 30), "6400", "  ")).IsFailure.Should().BeTrue(); // reason required

            var wo = await ar.WriteOffAsync(companyId, invoiceItemId, new DateOnly(2026, 4, 30), "6400", "Customer insolvent");
            wo.IsSuccess.Should().BeTrue(wo.IsFailure ? wo.Error.ToString() : null);

            // Nothing left to write off.
            (await ar.WriteOffAsync(companyId, invoiceItemId, new DateOnly(2026, 4, 30), "6400", "again")).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.OpenItems.FirstAsync(i => i.Id == invoiceItemId)).OutstandingTransaction.Should().Be(0m); // settled

            // The debtor no longer ages.
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            AgeingReport debtors = await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 4, 30));
            debtors.Rows.Should().NotContain(r => r.TraderCode == "BD-C");

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "6400" && r.Debit == 300m);   // bad-debt expense
            tb.Rows.Should().Contain(r => r.AccountCode == "4000" && r.Credit == 300m);  // revenue stands
            tb.Rows.Should().NotContain(r => r.AccountCode == "1200" && (r.Debit != 0m || r.Credit != 0m)); // AR control cleared
        }
    }
}
