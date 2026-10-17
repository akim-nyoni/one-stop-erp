using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers ageing and credit exposure as at a past date: a payment allocated after the date does not reduce the
/// past report, an un-allocation after the date does not either, and the current report reflects both.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AgeingAsAtTests
{
    private readonly AccountingFixture _fixture;

    public AgeingAsAtTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Ageing_as_at_a_past_date_ignores_later_allocations()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long allocationId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "AGA-C", "Aged Buyer", Scenario.Usd) { PaymentTermsDays = 30, CreditLimit = 5_000m };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();

            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            long invoice = (await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "AGA-I1", DocumentDate = new DateOnly(2026, 1, 15), Currency = Scenario.Usd, NetAmount = 1_000m, SalesAccountCode = "4000" })).Value.OpenItemId;

            // Paid in full on 20 April, allocated that day.
            long receipt = (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "AGA-R1", ReceiptDate = new DateOnly(2026, 4, 20), Currency = Scenario.Usd, Amount = 1_000m, BankAccountCode = "1100" })).Value.OpenItemId;
            allocationId = (await scope.ServiceProvider.GetRequiredService<IAllocationService>().AllocateAsync(companyId, TradingLedger.AccountsReceivable, invoice, receipt, 1_000m, new DateOnly(2026, 4, 20))).Value.AllocationId;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();

            // 31 March: the invoice (due 14 Feb) was 45 days overdue and unpaid.
            AgeingReport march = await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 3, 31));
            AgeingRow row = march.Rows.Single(r => r.TraderCode == "AGA-C");
            row.Days31To60.Should().Be(1_000m);
            row.Total.Should().Be(1_000m);
            (await reports.CreditExposureAsync(companyId, new DateOnly(2026, 3, 31))).Rows.Single(r => r.CustomerCode == "AGA-C").Balance.Should().Be(1_000m);

            // 30 April: settled.
            (await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 4, 30))).Rows.Should().NotContain(r => r.TraderCode == "AGA-C");

            // Aged by invoice date instead (the customer's own basis, thirteenth audit #12): 75 days since 15 January puts the same debt in 61–90.
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Customers.SingleAsync(c => c.CompanyId == companyId && c.Code == "AGA-C")).AgeingBasis = AgeingBasis.InvoiceDate;
            await db.SaveChangesAsync();
            (await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 3, 31))).Rows.Single(r => r.TraderCode == "AGA-C").Days61To90.Should().Be(1_000m);
            (await reports.CustomerStatementAsync(companyId, (await db.Customers.AsNoTracking().SingleAsync(c => c.CompanyId == companyId && c.Code == "AGA-C")).Id))!.AgeingBasis.Should().Be(AgeingBasis.InvoiceDate);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            // Un-allocated on 10 May: April stays settled, May shows the invoice and the unapplied receipt (net nil).
            (await scope.ServiceProvider.GetRequiredService<IAllocationService>().UnallocateAsync(companyId, allocationId, new DateOnly(2026, 5, 10))).IsSuccess.Should().BeTrue();

            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            (await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 4, 30))).Rows.Should().NotContain(r => r.TraderCode == "AGA-C");
            (await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 3, 31))).Rows.Single(r => r.TraderCode == "AGA-C").Total.Should().Be(1_000m);

            AgeingRow may = (await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 5, 31))).Rows.SingleOrDefault(r => r.TraderCode == "AGA-C")
                ?? new AgeingRow("AGA-C", string.Empty, 0m, 0m, 0m, 0m, 0m);
            may.Total.Should().Be(0m); // 1,000 invoice − 1,000 unapplied receipt
        }
    }
}
