using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Accounting;
using One4All.Core.Common;
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

    [SkippableFact]
    public async Task A_weekly_employees_foreign_table_is_scaled_to_the_week()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await ShillingsAsync(db);
        (PayElement basic, PayElement allowance, PayElement split) = await ElementsAsync(db, companyId);
        Employee waged = new(companyId, "SW-1", "Weekly Split", 300m, new DateOnly(2025, 1, 1)) { PayFrequency = PayFrequency.Weekly, PayGroup = "WEEKLY" };
        waged.AssignPayElement(basic.Id);
        waged.AssignPayElement(allowance.Id, overrideAmount: 100m);
        waged.AssignPayElement(split.Id);
        db.Employees.Add(waged);
        await db.SaveChangesAsync();

        // Both tables' monthly bands × 12/52. Base: 20% of 400 less 115.38 = 56.92, 300/400 of it on the dollars = 42.69. The 100 paid in
        // shillings is 250,000 TSh, 10% above 115,384.62 = 13,461.54 TSh = 5.38: 48.07 in all (unscaled the shillings would bear nothing).
        IPayrollService payroll = scope.ServiceProvider.GetRequiredService<IPayrollService>();
        Result<PayrollRunResult> run = await payroll.RunGroupAsync(companyId, "WEEKLY", "2026-W18", new DateOnly(2026, 5, 1), "2420", "SW-W18");
        run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.ToString() : null);
        PayslipLine tax = (await db.Set<Payslip>().AsNoTracking().Include(p => p.Lines).SingleAsync(p => p.EmployeeId == waged.Id)).Lines.Single(l => l.ElementCode == "PAYE-S");
        tax.Amount.Should().Be(48.07m);
        tax.CurrencySplit.Should().Be("TZS=5.38");
    }

    [SkippableFact]
    public async Task Tax_credits_come_off_the_split_tax_and_its_currency_shares()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await ShillingsAsync(db);
        (PayElement basic, PayElement allowance, PayElement split) = await ElementsAsync(db, companyId);
        db.Add(new TaxCredit(companyId, "ELDER", "Elderly credit") { Basis = TaxCreditBasis.Fixed, Amount = 50m, AppliesToAll = true });
        Employee employee = new(companyId, "SK-1", "Credited Split", 1_000m, new DateOnly(2025, 1, 1));
        employee.AssignPayElement(basic.Id);
        employee.AssignPayElement(allowance.Id, overrideAmount: 500m);
        employee.AssignPayElement(split.Id);
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        // Before credits 133.33 on the dollars and 30.00 on the shillings = 163.33; the 50 credit comes off the total (113.33), each
        // currency's share reduced in proportion: the shillings bear 30 × 113.33 / 163.33 = 20.82.
        Result<PayrollRunResult> run = await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-04", new DateOnly(2026, 4, 25), "2420", "SK-04");
        run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.ToString() : null);
        PayslipLine tax = (await db.Set<Payslip>().AsNoTracking().Include(p => p.Lines).SingleAsync(p => p.EmployeeId == employee.Id)).Lines.Single(l => l.ElementCode == "PAYE-S");
        tax.Amount.Should().Be(113.33m);
        tax.CurrencySplit.Should().Be("TZS=20.82");
    }

    [SkippableFact]
    public async Task Cumulative_tax_cannot_also_split_by_currency()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        PayElement tax = new(companyId, "PAYE-C", "PAYE (cumulative)", PayElementType.Deduction, CalculationBasis.TaxTable, 3, "2400");
        db.PayElements.Add(tax);
        await db.SaveChangesAsync();
        IPayrollSetupService setup = scope.ServiceProvider.GetRequiredService<IPayrollSetupService>();
        (await setup.SetPayElementOptionsAsync(companyId, "PAYE-C", new PayElementOptions(CumulativeTax: true, SplitCurrencyTax: true))).Error.Code.Should().Be("Payroll.CumulativeSplitTax");
        (await db.PayElements.AsNoTracking().SingleAsync(e => e.Id == tax.Id)).CumulativeTax.Should().BeFalse();
        (await setup.SetPayElementOptionsAsync(companyId, "PAYE-C", new PayElementOptions(CumulativeTax: true))).IsSuccess.Should().BeTrue();
    }

    private static async Task ShillingsAsync(One4AllDbContext db)
    {
        if (!await db.Currencies.AnyAsync(c => c.Code == Tzs))
        {
            db.Currencies.Add(new Currency(Tzs, "Tanzanian Shilling", "TSh"));
        }

        if (!await db.ExchangeRates.AnyAsync(r => r.FromCurrency == Tzs && r.ToCurrency == Scenario.Usd && r.RateType == ExchangeRateType.Daily))
        {
            db.ExchangeRates.Add(new ExchangeRate(Tzs, Scenario.Usd, ExchangeRateType.Daily, new DateOnly(2026, 1, 1), 0.0004m));   // 2,500 a dollar
        }

        await db.SaveChangesAsync();
    }

    // Monthly tables: dollars nil to 500 then 20%; shillings nil to 500,000 then 10%. Basic, an allowance paid in shillings and split-currency PAYE.
    private static async Task<(PayElement Basic, PayElement Allowance, PayElement Split)> ElementsAsync(One4AllDbContext db, long companyId)
    {
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
        db.PayElements.AddRange(basic, allowance, split);
        await db.SaveChangesAsync();
        return (basic, allowance, split);
    }

    [SkippableFact]
    public async Task Amounts_keyed_in_the_paid_in_currency_come_into_base_at_the_runs_rate()
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

        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        PayElement local = new(companyId, "LOCAL", "Allowance keyed in shillings", PayElementType.Earning, CalculationBasis.FixedAmount, 2, "6100")
        {
            Taxable = true, PaidInCurrency = Tzs, AmountsInPaidCurrency = true,
        };
        db.PayElements.AddRange(basic, local);
        await db.SaveChangesAsync();
        Employee employee = new(companyId, "AC-1", "Amount Currency", 1_000m, new DateOnly(2025, 1, 1));
        employee.AssignPayElement(basic.Id);
        employee.AssignPayElement(local.Id, overrideAmount: 1_250_000m);
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        // TSh 1,250,000 at 0.0004 is 500.00 in base: gross 1,500, and the line says what was keyed and the rate.
        PayrollRunResult run = (await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-05", new DateOnly(2026, 5, 25), "2420", "AC-05")).Value;
        run.TotalGross.Should().Be(1_500m);
        PayslipLine line = (await db.Set<Payslip>().AsNoTracking().Include(p => p.Lines).SingleAsync(p => p.EmployeeId == employee.Id)).Lines.Single(l => l.ElementCode == "LOCAL");
        line.Amount.Should().Be(500m);
        line.Workings.Should().Contain("TZS 1,250,000.00 at 0.0004");
    }
}
