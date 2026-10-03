using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers interest exemptions and compounding: three accounts each owe 1,000 from 1 January at 36.5% a year; the account
/// flagged exempt and the indigent ratepayer are not charged, the other is charged 30 for January; in the next run the
/// compounding rule also charges interest on that 30 (0.90), which the default rule would not.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class FinanceChargeExemptionTests
{
    private readonly AccountingFixture _fixture;

    public FinanceChargeExemptionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Exempt_accounts_are_skipped_and_compounding_charges_interest_on_interest()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer payer = new(companyId, "FX-PAY", "Ordinary Debtor", Scenario.Usd) { PaymentTermsDays = 0 };
        Customer exempt = new(companyId, "FX-GOV", "Government Department", Scenario.Usd) { PaymentTermsDays = 0, InterestExempt = true };
        Customer indigent = new(companyId, "FX-IND", "Indigent Household", Scenario.Usd) { PaymentTermsDays = 0 };
        db.Customers.AddRange(payer, exempt, indigent);
        await db.SaveChangesAsync();
        Property home = new(companyId, "901", "Ward 10", indigent.Id);
        db.Properties.Add(home);
        await db.SaveChangesAsync();
        db.Set<IndigentRegistration>().Add(new IndigentRegistration(companyId, home.Id, 50m, 100m, new DateOnly(2025, 12, 1), new DateOnly(2026, 12, 1), 71));
        await db.SaveChangesAsync();

        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        foreach (Customer customer in new[] { payer, exempt, indigent })
        {
            (await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = $"{customer.Code}-I", DocumentDate = new DateOnly(2026, 1, 1), Currency = Scenario.Usd, NetAmount = 1000m, SalesAccountCode = "4000",
            })).IsSuccess.Should().BeTrue();
        }

        IFinanceChargeService charges = scope.ServiceProvider.GetRequiredService<IFinanceChargeService>();
        FinanceChargeRunRequest Run(DateOnly asAt, bool compound, string prefix) => new()
        {
            CompanyId = companyId, AsAtDate = asAt, AnnualRatePercent = 36.5m, InterestIncomeAccountCode = "4900", DocumentPrefix = prefix, Compound = compound, ExemptIndigent = true,
        };

        FinanceChargeRunResult january = (await charges.RaiseChargesAsync(Run(new DateOnly(2026, 1, 31), false, "FCJ"))).Value;
        january.Charges.Select(c => c.CustomerId).Should().Equal(payer.Id);
        january.TotalInterest.Should().Be(30m);
        january.CustomersExempted.Should().Be(2);

        FinanceChargeRunResult february = (await charges.RaiseChargesAsync(Run(new DateOnly(2026, 3, 2), true, "FCF"))).Value;
        february.TotalInterest.Should().Be(30.90m);
    }
}
