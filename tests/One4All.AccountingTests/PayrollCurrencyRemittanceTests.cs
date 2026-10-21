using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Accounting;
using One4All.Core.Payroll;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers remitting tax in the currency the earnings were paid in: each deduction line carries the base amounts belonging to each foreign
/// currency (split-currency tax as it was worked out, a levy on it in the same proportions, anything else by taxable earnings), and the
/// authority's invoices are raised per currency, the foreign part in that currency at the pay date's rate.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayrollCurrencyRemittanceTests
{
    private static readonly CurrencyCode Dkk = CurrencyCode.Parse("DKK");

    private readonly AccountingFixture _fixture;

    public PayrollCurrencyRemittanceTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Tax_and_levy_are_remitted_in_the_currency_the_earnings_were_paid_in()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        if (!await db.Currencies.AnyAsync(c => c.Code == Dkk))
        {
            db.Currencies.Add(new Currency(Dkk, "Danish Krone", "kr"));
        }

        if (!await db.ExchangeRates.AnyAsync(r => r.FromCurrency == Dkk && r.ToCurrency == Scenario.Usd && r.RateType == ExchangeRateType.Daily))
        {
            db.ExchangeRates.Add(new ExchangeRate(Dkk, Scenario.Usd, ExchangeRateType.Daily, new DateOnly(2026, 1, 1), 0.1m));   // 10 kroner a dollar
        }

        Company company = await db.Companies.SingleAsync(c => c.Id == companyId);
        company.RaisePayrollRemittances = false;
        Supplier taxman = new(companyId, "MC-TAX", "Revenue Authority", Scenario.Usd);
        db.Suppliers.Add(taxman);
        TaxTable dollars = new(companyId, "PAYE", new DateOnly(2026, 1, 1));
        dollars.AddBand(0m, 500m, 0m);
        dollars.AddBand(500m, null, 30m);
        TaxTable kroner = new(companyId, "PAYE-DKK", new DateOnly(2026, 1, 1), Dkk);
        kroner.AddBand(0m, 2_000m, 0m);
        kroner.AddBand(2_000m, null, 10m);
        db.AddRange(dollars, kroner);
        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        PayElement local = new(companyId, "LOCAL", "Allowance paid in kroner", PayElementType.Earning, CalculationBasis.FixedAmount, 2, "6100") { Taxable = true, PaidInCurrency = Dkk };
        PayElement paye = new(companyId, "PAYE", "PAYE", PayElementType.Deduction, CalculationBasis.TaxTable, 3, "2400") { SplitCurrencyTax = true };
        PayElement levy = new(companyId, "AIDS", "AIDS levy", PayElementType.Deduction, CalculationBasis.Formula, 4, "2400") { Formula = "PAYE * 3 / 100" };
        PayElement medical = new(companyId, "MED", "Medical aid", PayElementType.Deduction, CalculationBasis.FixedAmount, 5, "2410") { FixedAmount = 30m };
        db.PayElements.AddRange(basic, local, paye, levy, medical);
        await db.SaveChangesAsync();
        db.Add(new StatutoryScheme(companyId, "PAYE", "Income tax", SchemeKind.TaxAuthority, "Revenue Authority", "PAYE", null) { SupplierId = taxman.Id });
        db.Add(new StatutoryScheme(companyId, "AIDS", "AIDS levy", SchemeKind.TaxAuthority, "Revenue Authority", "AIDS", null) { SupplierId = taxman.Id });
        Employee split = new(companyId, "MC-1", "Split Paid", 1_000m, new DateOnly(2025, 1, 1)), dollarsOnly = new(companyId, "MC-2", "Dollars Only", 1_000m, new DateOnly(2025, 1, 1));
        foreach (Employee e in new[] { split, dollarsOnly })
        {
            e.AssignPayElement(basic.Id);
            e.AssignPayElement(paye.Id);
            e.AssignPayElement(levy.Id);
            e.AssignPayElement(medical.Id);
        }

        split.AssignPayElement(local.Id, overrideAmount: 500m);
        db.Employees.AddRange(split, dollarsOnly);
        await db.SaveChangesAsync();

        // Split Paid: 1,500 taxable; the base table's 300 on it is shared 200 to the dollars, and the 500 paid in kroner (5,000 kr) is taxed
        // 300 kr on its own table = 30.00: PAYE 230, 30 of it kroner. The levy 6.90 follows PAYE (0.90 kroner); medical aid by earnings (10.00).
        // Dollars Only has no foreign earnings: PAYE 150, levy 4.50, no splits.
        IPayrollService payroll = scope.ServiceProvider.GetRequiredService<IPayrollService>();
        var run = await payroll.RunAsync(companyId, "2026-04", new DateOnly(2026, 4, 25), "2420", "MC-04");
        run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.ToString() : null);
        List<PayslipLine> lines = (await db.Set<Payslip>().AsNoTracking().Include(p => p.Lines).SingleAsync(p => p.EmployeeId == split.Id)).Lines.ToList();
        lines.Single(l => l.ElementCode == "PAYE").Should().Match<PayslipLine>(l => l.Amount == 230m && l.CurrencySplit == "DKK=30.00");
        lines.Single(l => l.ElementCode == "AIDS").Should().Match<PayslipLine>(l => l.Amount == 6.9m && l.CurrencySplit == "DKK=0.90");
        lines.Single(l => l.ElementCode == "MED").CurrencySplit.Should().Be("DKK=10.00");
        lines.Where(l => l.Type == PayElementType.Earning).Should().OnlyContain(l => l.CurrencySplit == null);
        (await db.Set<Payslip>().AsNoTracking().Include(p => p.Lines).SingleAsync(p => p.EmployeeId == dollarsOnly.Id)).Lines.Should().OnlyContain(l => l.CurrencySplit == null);

        // PAYE: 200 + 150 in dollars, 30.00 as 300 kr; the levy: 6.00 + 4.50 in dollars, 0.90 as 9 kr.
        var raised = await scope.ServiceProvider.GetRequiredService<IPayrollRemittanceService>().RaiseRemittancesAsync(companyId, "MC-04");
        raised.IsSuccess.Should().BeTrue(raised.IsFailure ? raised.Error.ToString() : null);
        raised.Value.Select(r => (r.DocumentNumber, r.Amount)).Should().Equal(("MC-04-AIDS", 10.5m), ("MC-04-AIDS-DKK", 0.9m), ("MC-04-PAYE", 350m), ("MC-04-PAYE-DKK", 30m));
        var invoices = await db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsPayable && i.TraderId == taxman.Id)
            .Select(i => new { i.DocumentNumber, i.Currency, i.ExchangeRate, i.OriginalTransaction, i.OriginalBase }).ToListAsync();
        invoices.Should().HaveCount(4);
        var payeKroner = invoices.Single(i => i.DocumentNumber == "MC-04-PAYE-DKK");
        payeKroner.Currency.Should().Be(Dkk);
        payeKroner.ExchangeRate.Should().Be(0.1m);
        Math.Abs(payeKroner.OriginalTransaction).Should().Be(300m);
        Math.Abs(payeKroner.OriginalBase).Should().Be(30m);
        var levyKroner = invoices.Single(i => i.DocumentNumber == "MC-04-AIDS-DKK");
        levyKroner.Currency.Should().Be(Dkk);
        Math.Abs(levyKroner.OriginalTransaction).Should().Be(9m);
        var payeDollars = invoices.Single(i => i.DocumentNumber == "MC-04-PAYE");
        payeDollars.Currency.Should().Be(Scenario.Usd);
        Math.Abs(payeDollars.OriginalTransaction).Should().Be(350m);
        (await db.RemittanceInvoices.AsNoTracking().Where(r => r.CompanyId == companyId).SumAsync(r => r.Amount)).Should().Be(391.4m);
    }
}
