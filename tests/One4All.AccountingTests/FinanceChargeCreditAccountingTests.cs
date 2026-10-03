using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers finance charges with unallocated receipts: interest stops on the part a receipt covers from the receipt's
/// date, so a customer who paid in full on 2 March is charged 60 days, not 90; a part-payer is charged on both
/// pieces; and the next month's run charges only the part still unpaid.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class FinanceChargeCreditAccountingTests
{
    private readonly AccountingFixture _fixture;

    public FinanceChargeCreditAccountingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Unallocated_receipts_stop_interest_from_their_date()
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
            Customer a = new(companyId, "FC-PAID", "Paid In March", Scenario.Usd) { PaymentTermsDays = 0 };
            Customer b = new(companyId, "FC-PART", "Part Paid", Scenario.Usd) { PaymentTermsDays = 0 };
            db.Customers.AddRange(a, b);
            await db.SaveChangesAsync();
            (paid, partly) = (a.Id, b.Id);

            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            foreach ((Customer customer, decimal receipt) in new[] { (a, 1_000m), (b, 400m) })
            {
                await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = $"{customer.Code}-I", DocumentDate = new DateOnly(2026, 1, 1), Currency = Scenario.Usd, NetAmount = 1_000m, SalesAccountCode = "4000" });
                await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = $"{customer.Code}-R", ReceiptDate = new DateOnly(2026, 3, 2), Currency = Scenario.Usd, Amount = receipt, BankAccountCode = "1100" });
            }
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IFinanceChargeService charges = scope.ServiceProvider.GetRequiredService<IFinanceChargeService>();
            FinanceChargeRunResult april = (await charges.RaiseChargesAsync(Request(companyId, new DateOnly(2026, 4, 1)))).Value;

            april.Charges.Single(c => c.CustomerId == paid).Should().BeEquivalentTo(new { Interest = 19.73m, OverdueAmount = 0m });   // 1,000 × 60 days
            april.Charges.Single(c => c.CustomerId == partly).Should().BeEquivalentTo(new { Interest = 25.64m, OverdueAmount = 600m }); // 400 × 60 + 600 × 90

            // May: the paid customer owes no more interest; the part-payer is charged 30 days on the unpaid 600.
            FinanceChargeRunResult may = (await charges.RaiseChargesAsync(Request(companyId, new DateOnly(2026, 5, 1)))).Value;
            may.Charges.Should().NotContain(c => c.CustomerId == paid);
            may.Charges.Single(c => c.CustomerId == partly).Interest.Should().Be(5.92m);
        }
    }

    private static FinanceChargeRunRequest Request(long companyId, DateOnly asAt) => new()
    {
        CompanyId = companyId, AsAtDate = asAt, AnnualRatePercent = 12m, GraceDays = 0, MinimumCharge = 0m, InterestIncomeAccountCode = "4800",
    };
}
