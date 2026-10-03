using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the credit review netting unallocated credits: a customer whose unallocated receipt covers the overdue
/// invoice is not put on stop, and one whose receipt covers only part of it is held for the remainder.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class CreditReviewCreditsTests
{
    private readonly AccountingFixture _fixture;

    public CreditReviewCreditsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Unallocated_receipts_keep_paid_customers_off_stop()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long paid;
        long partly;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer a = new(companyId, "CR-PAID", "Paid Ltd", Scenario.Usd) { PaymentTermsDays = 30 };
            Customer b = new(companyId, "CR-PART", "Part Paid Ltd", Scenario.Usd) { PaymentTermsDays = 30 };
            db.Customers.AddRange(a, b);
            await db.SaveChangesAsync();
            (paid, partly) = (a.Id, b.Id);

            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = a.Id, DocumentNumber = "CR-A1", DocumentDate = new DateOnly(2026, 2, 1), Currency = Scenario.Usd, NetAmount = 250m, SalesAccountCode = "4000" });
            await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = a.Id, DocumentNumber = "CR-AR", ReceiptDate = new DateOnly(2026, 5, 20), Currency = Scenario.Usd, Amount = 250m, BankAccountCode = "1100" });
            await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = b.Id, DocumentNumber = "CR-B1", DocumentDate = new DateOnly(2026, 2, 1), Currency = Scenario.Usd, NetAmount = 400m, SalesAccountCode = "4000" });
            await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = b.Id, DocumentNumber = "CR-BR", ReceiptDate = new DateOnly(2026, 5, 20), Currency = Scenario.Usd, Amount = 150m, BankAccountCode = "1100" });
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            CreditReviewResult review = (await scope.ServiceProvider.GetRequiredService<ICreditControlService>().ReviewAsync(companyId, new DateOnly(2026, 6, 1), overdueToleranceDays: 14)).Value;

            review.Held.Should().NotContain(h => h.CustomerId == paid);
            review.Held.Should().ContainSingle(h => h.CustomerId == partly).Which.Should().BeEquivalentTo(new { Reason = CreditHoldReason.Overdue, OverdueAmount = 250m });

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Customers.FindAsync(paid))!.OnHold.Should().BeFalse();
        }
    }
}
