using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Accounting;
using One4All.Core.Payroll;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Reporting;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the PAYE return (PR27) per currency the earnings were paid in - taxable earnings, PAYE and the AIDS levy in base and in the currency at
/// the pay date's rate, from the lines' currency splits - and the tax certificates (PR28) with the AIDS levy, social security and medical aid; tax,
/// levy and contributions are recognised by the element's statutory flag whatever the element is called.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayeReturnByCurrencyTests
{
    private static readonly CurrencyCode Isk = CurrencyCode.Parse("ISK");

    private readonly AccountingFixture _fixture;

    public PayeReturnByCurrencyTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task The_return_shows_paye_and_levy_per_currency_and_the_certificate_the_levy_and_contributions()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        if (!await db.Currencies.AnyAsync(c => c.Code == Isk))
        {
            db.Currencies.Add(new Currency(Isk, "Icelandic Krona", "kr"));
        }

        if (!await db.ExchangeRates.AnyAsync(r => r.FromCurrency == Isk && r.ToCurrency == Scenario.Usd && r.RateType == ExchangeRateType.Daily))
        {
            db.ExchangeRates.Add(new ExchangeRate(Isk, Scenario.Usd, ExchangeRateType.Daily, new DateOnly(2026, 1, 1), 0.1m));   // 10 kronur a dollar
        }

        (await db.Companies.SingleAsync(c => c.Id == companyId)).RaisePayrollRemittances = false;
        TaxTable dollars = new(companyId, "PAYE", new DateOnly(2026, 1, 1));
        dollars.AddBand(0m, 500m, 0m);
        dollars.AddBand(500m, null, 30m);
        TaxTable kronur = new(companyId, "PAYE-ISK", new DateOnly(2026, 1, 1), Isk);
        kronur.AddBand(0m, 2_000m, 0m);
        kronur.AddBand(2_000m, null, 10m);
        db.AddRange(dollars, kronur);

        // The tax and the levy are called neither PAYE / tax nor AIDS: only their flags make them so.
        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        PayElement local = new(companyId, "LOCAL", "Allowance paid in kronur", PayElementType.Earning, CalculationBasis.FixedAmount, 2, "6100") { Taxable = true, PaidInCurrency = Isk };
        PayElement paye = new(companyId, "IT", "Income withholding", PayElementType.Deduction, CalculationBasis.TaxTable, 3, "2400") { SplitCurrencyTax = true, StatutoryKind = StatutoryKind.Paye };
        PayElement levy = new(companyId, "HLEVY", "Health surcharge", PayElementType.Deduction, CalculationBasis.Formula, 4, "2400") { Formula = "IT * 3 / 100", StatutoryKind = StatutoryKind.AidsLevy };
        PayElement social = new(companyId, "SOC", "Pension and welfare", PayElementType.Deduction, CalculationBasis.FixedAmount, 5, "2410") { FixedAmount = 20m, StatutoryKind = StatutoryKind.SocialSecurity };
        PayElement socialEmployer = new(companyId, "SOCER", "Employer welfare", PayElementType.EmployerContribution, CalculationBasis.FixedAmount, 6, "2410")
        {
            FixedAmount = 25m, ContributionExpenseAccountCode = "6100", StatutoryKind = StatutoryKind.SocialSecurity,
        };
        PayElement medical = new(companyId, "MED", "Health cover", PayElementType.Deduction, CalculationBasis.FixedAmount, 7, "2410") { FixedAmount = 30m, StatutoryKind = StatutoryKind.MedicalAid };
        db.PayElements.AddRange(basic, local, paye, levy, social, socialEmployer, medical);
        await db.SaveChangesAsync();
        Employee split = new(companyId, "PR-1", "Split Paid", 1_000m, new DateOnly(2025, 1, 1)), dollarsOnly = new(companyId, "PR-2", "Dollars Only", 1_000m, new DateOnly(2025, 1, 1));
        foreach (Employee e in new[] { split, dollarsOnly })
        {
            foreach (PayElement element in new[] { basic, paye, levy, social, socialEmployer, medical })
            {
                e.AssignPayElement(element.Id);
            }
        }

        split.AssignPayElement(local.Id, overrideAmount: 500m);
        db.Employees.AddRange(split, dollarsOnly);
        await db.SaveChangesAsync();

        // Split Paid: 1,500 taxable, PAYE 230 of which 30.00 falls on the kronur (300 kr on their own table), levy 6.90 (0.90 kronur).
        // Dollars Only: 1,000 taxable, PAYE 150, levy 4.50.
        var run = await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-04", new DateOnly(2026, 4, 25), "2420", "PR27-04");
        run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.ToString() : null);
        List<PayslipLine> lines = (await db.Set<Payslip>().AsNoTracking().Include(p => p.Lines).SingleAsync(p => p.EmployeeId == split.Id)).Lines.ToList();
        lines.Single(l => l.ElementCode == "IT").Should().Match<PayslipLine>(l => l.Amount == 230m && l.CurrencySplit == "ISK=30.00");
        lines.Single(l => l.ElementCode == "HLEVY").Should().Match<PayslipLine>(l => l.Amount == 6.9m && l.CurrencySplit == "ISK=0.90");

        PayrollReportDataProvider reports = new(db);
        Dictionary<string, object?> period = new() { ["FromDate"] = new DateOnly(2026, 4, 1), ["ToDate"] = new DateOnly(2026, 4, 30) };
        IReadOnlyList<IReadOnlyDictionary<string, object?>> payeReturn = await reports.GetRowsAsync(companyId, "PyrlPayeReturn", period);
        payeReturn.Should().HaveCount(2);
        IReadOnlyDictionary<string, object?> usd = payeReturn[0], isk = payeReturn[1];
        usd["Currency"].Should().Be("USD");
        usd["Employees"].Should().Be(2);
        usd["Taxable"].Should().Be("2,000.00");
        ((decimal)usd["Deducted"]!).Should().Be(350m);
        usd["Levy"].Should().Be("10.50");
        usd["ByTable"].Should().Be("350.00");     // 200 of Split Paid's 300 by the table (the dollars' share) and Dollars Only's 150
        usd["Difference"].Should().Be("0.00");
        usd["PayeInCurrency"].Should().Be("350.00");

        isk["Currency"].Should().Be("ISK");
        isk["Employees"].Should().Be(1);
        isk["Rate"].Should().Be("0.1");
        isk["Taxable"].Should().Be("500.00");
        isk["TaxableInCurrency"].Should().Be("5,000.00");
        ((decimal)isk["Deducted"]!).Should().Be(30m);
        isk["PayeInCurrency"].Should().Be("300.00");
        isk["Levy"].Should().Be("0.90");
        isk["LevyInCurrency"].Should().Be("9.00");
        payeReturn.Sum(r => (decimal)r["Deducted"]!).Should().Be(380m, "the currencies add up to the PAYE on the payslips");

        IReadOnlyList<IReadOnlyDictionary<string, object?>> certificates = await reports.GetRowsAsync(companyId, "PyrlCertificates", period);
        IReadOnlyDictionary<string, object?> certificate = certificates.Single(r => (string)r["Employee"]! == "PR-1 Split Paid");
        ((decimal)certificate["Paye"]!).Should().Be(230m);
        certificate["AidsLevy"].Should().Be("6.90");
        certificate["SocialSecurity"].Should().Be("20.00");
        certificate["SocialSecurityEmployer"].Should().Be("25.00");
        certificate["Medical"].Should().Be("30.00");
    }
}
