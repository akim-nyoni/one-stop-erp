using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers repeated finance-charge runs: each run charges only the days since the last one, a re-run of the same
/// date charges nothing, and earlier finance charges never earn interest themselves.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class FinanceChargeRerunTests
{
    private readonly AccountingFixture _fixture;

    public FinanceChargeRerunTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Monthly_runs_charge_each_day_once_and_never_interest_on_interest()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "FCR-C", "Slow Payer", Scenario.Usd) { PaymentTermsDays = 0 };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;

            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            (await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = customerId, DocumentNumber = "FCR-INV", DocumentDate = new DateOnly(2026, 1, 1),
                Currency = Scenario.Usd, NetAmount = 1_000m, TaxAmount = 0m, SalesAccountCode = "4000",
            })).IsSuccess.Should().BeTrue();
        }

        // Run 1 at 31 Jan: 30 days on 1,000 at 12% = 9.86.
        FinanceChargeRunResult january = await Run(provider, companyId, new DateOnly(2026, 1, 31));
        january.TotalInterest.Should().Be(9.86m);

        // Same date again: nothing new to charge.
        (await Run(provider, companyId, new DateOnly(2026, 1, 31))).Charges.Should().BeEmpty();

        // Run 2 at 2 Mar: only the 30 days since 31 Jan - not 60 - and nothing on January's 9.86 finance charge.
        FinanceChargeRunResult march = await Run(provider, companyId, new DateOnly(2026, 3, 2));
        CustomerFinanceCharge charge = march.Charges.Should().ContainSingle().Subject;
        charge.Interest.Should().Be(9.86m);
        charge.MaxDaysOverdue.Should().Be(60);   // ageing still reports the true age
        charge.OverdueAmount.Should().Be(1_000m); // finance charges are not part of the interest-bearing balance

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            List<OpenItem> finance = await db.OpenItems.Where(o => o.CompanyId == companyId && o.TraderId == customerId && o.ItemType == OpenItemType.Interest).ToListAsync();
            finance.Should().HaveCount(2);
            finance.Should().OnlyContain(o => o.Sign == LedgerSign.Debit && o.OriginalBase == 9.86m);
            (await db.OpenItems.SingleAsync(o => o.CompanyId == companyId && o.DocumentNumber == "FCR-INV")).InterestChargedTo.Should().Be(new DateOnly(2026, 3, 2));

            // Interest income is exactly two months' worth on a balanced ledger.
            IConsolidationService tb = scope.ServiceProvider.GetRequiredService<IConsolidationService>();
            ConsolidatedTrialBalance balance = (await tb.ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 3, 31))).Value;
            balance.IsBalanced.Should().BeTrue();
            balance.Rows.Single(r => r.AccountCode == "4800").Credit.Should().Be(19.72m);

            // The finance charge prints as a document.
            IInvoiceDocumentService documents = scope.ServiceProvider.GetRequiredService<IInvoiceDocumentService>();
            (await documents.RenderTextAsync(companyId, finance[0].Id)).IsSuccess.Should().BeTrue();
        }
    }

    private static async Task<FinanceChargeRunResult> Run(ServiceProvider provider, long companyId, DateOnly asAt)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IFinanceChargeService>().RaiseChargesAsync(new FinanceChargeRunRequest
        {
            CompanyId = companyId, AsAtDate = asAt, AnnualRatePercent = 12m, GraceDays = 0, MinimumCharge = 0m, InterestIncomeAccountCode = "4800",
        });
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
        return result.Value;
    }
}
