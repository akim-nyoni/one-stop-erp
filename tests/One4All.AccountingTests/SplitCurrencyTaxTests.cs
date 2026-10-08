using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Accounting;
using One4All.Core.Payroll;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers split-currency tax in the pay run: earnings paid in a currency with its own tax table are taxed on it at the pay date's rate, the rest
/// bears its share of the base table's tax on the whole.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SplitCurrencyTaxTests
{
    private static readonly CurrencyCode Tzs = CurrencyCode.Parse("TZS");

    private readonly AccountingFixture _fixture;

    public SplitCurrencyTaxTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Each_currency_is_taxed_on_its_own_table()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        if (!await db.Currencies.AnyAsync(c => c.Code == Tzs))
        {
            db.Currencies.Add(new Currency(Tzs, "Tanzanian Shilling", "TSh"));
        }

        if (!await db.ExchangeRates.AnyAsync(r => r.FromCurrency == Tzs && r.ToCurrency == Scenario.Usd && r.RateType == ExchangeRateType.Daily))
        {
            db.ExchangeRates.Add(new ExchangeRate(Tzs, Scenario.Usd, ExchangeRateType.Daily, new DateOnly(2026, 1, 1), 0.0004m));   // 2,500 a dollar
        }

        TaxTable dollars = new(companyId, "PAYE", new DateOnly(2026, 1, 1));
        dollars.AddBand(0m, 500m, 0m);
        dollars.AddBand(500m, null, 20m);
        TaxTable shillings = new(companyId, "PAYE-TZS", new DateOnly(2026, 1, 1), Tzs);
        shillings.AddBand(0m, 500_000m, 0m);
        shillings.AddBand(500_000m, null, 10m);
        db.AddRange(dollars, shillings);
        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        PayElement allowance = new(companyId, "LOCAL", "Allowance paid in shillings", PayElementType.Earning, CalculationBasis.FixedAmount, 2, "6100") { Taxable = true, PaidInCurrency = Tzs };
        PayElement split = new(companyId, "PAYE-S", "PAYE (split)", PayElementType.Deduction, CalculationBasis.TaxTable, 3, "2400") { SplitCurrencyTax = true };
        PayElement whole = new(companyId, "PAYE-W", "PAYE (whole)", PayElementType.Deduction, CalculationBasis.TaxTable, 3, "2400");
        db.PayElements.AddRange(basic, allowance, split, whole);
        await db.SaveChangesAsync();
        Employee splitPaid = new(companyId, "SC-1", "Split Paid", 1_000m, new DateOnly(2025, 1, 1)), wholePaid = new(companyId, "SC-2", "Whole Paid", 1_000m, new DateOnly(2025, 1, 1));
        foreach ((Employee employee, PayElement tax) in new[] { (splitPaid, split), (wholePaid, whole) })
        {
            employee.AssignPayElement(basic.Id);
            employee.AssignPayElement(allowance.Id, overrideAmount: 500m);
            employee.AssignPayElement(tax.Id);
        }

        db.Employees.AddRange(splitPaid, wholePaid);
        await db.SaveChangesAsync();

        // 1,500 taxable: the base table's tax on it is 200, 1,000/1,500 of which is the dollars' 133.33; the 500 paid in shillings is
        // 1,250,000 TSh, taxed 75,000 TSh on their own table = 30.00. Taxed whole on the base table it would be 200.
        IPayrollService payroll = scope.ServiceProvider.GetRequiredService<IPayrollService>();
        PayrollRunResult run = (await payroll.RunAsync(companyId, "2026-04", new DateOnly(2026, 4, 25), "2420", "SC-04")).Value;
        run.TotalDeductions.Should().Be(163.33m + 200m);
        (await db.Set<Payslip>().AsNoTracking().Include(p => p.Lines).SingleAsync(p => p.EmployeeId == splitPaid.Id)).Lines.Single(l => l.ElementCode == "PAYE-S").Amount.Should().Be(163.33m);
    }
}
