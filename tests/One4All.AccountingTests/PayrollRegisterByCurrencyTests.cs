using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Payroll;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the payroll register by currency (PR75): per run and currency, the gross paid in it, the deductions falling on it by the lines'
/// currency splits, and the net paid in it as the settlement pays it - earnings paid in the currency first, then splits paid in it.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayrollRegisterByCurrencyTests
{
    private static readonly CurrencyCode Ugx = CurrencyCode.Parse("UGX");
    private readonly AccountingFixture _fixture;

    public PayrollRegisterByCurrencyTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Each_run_is_listed_by_the_currencies_it_is_paid_in()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        long companyId = await Scenario.SeedAsync(db);
        if (!await db.Currencies.AnyAsync(c => c.Code == Ugx))
        {
            db.Currencies.Add(new Currency(Ugx, "Ugandan Shilling", "USh"));
        }

        if (!await db.ExchangeRates.AnyAsync(r => r.FromCurrency == Ugx && r.ToCurrency == Scenario.Usd && r.RateType == ExchangeRateType.Daily))
        {
            db.ExchangeRates.Add(new ExchangeRate(Ugx, Scenario.Usd, ExchangeRateType.Daily, new DateOnly(2026, 1, 1), 0.00025m));   // 4,000 a dollar
        }

        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        PayElement local = new(companyId, "LOCAL", "Allowance paid in shillings", PayElementType.Earning, CalculationBasis.FixedAmount, 2, "6100") { FixedAmount = 400m, Taxable = true, PaidInCurrency = Ugx };
        PayElement paye = new(companyId, "PAYE", "PAYE", PayElementType.Deduction, CalculationBasis.Formula, 3, "2400") { Formula = "TAXABLE * 10 / 100" };
        PayElement union = new(companyId, "UNION", "Union dues", PayElementType.Deduction, CalculationBasis.FixedAmount, 4, "2400") { FixedAmount = 20m };
        db.PayElements.AddRange(basic, local, paye, union);
        await db.SaveChangesAsync();
        Employee mixed = new(companyId, "RB-1", "Mixed Pay", 1_000m, new DateOnly(2025, 1, 1));
        Employee halved = new(companyId, "RB-2", "Half Split", 600m, new DateOnly(2025, 1, 1));
        mixed.AssignPayElement(basic.Id);
        mixed.AssignPayElement(local.Id);
        mixed.AssignPayElement(paye.Id);
        mixed.AssignPayElement(union.Id);
        halved.AssignPayElement(basic.Id);
        halved.AssignPayElement(paye.Id);
        db.Employees.AddRange(mixed, halved);
        await db.SaveChangesAsync();
        db.PaymentSplits.Add(new EmployeePaymentSplit(companyId, halved.Id, PayMethod.BankTransfer, "UG-001", 0m, 50m) { Currency = Ugx });
        await db.SaveChangesAsync();
        var run = await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-05", new DateOnly(2026, 5, 25), "2420", "RB-05");
        run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.ToString() : null);

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(Dictionary<string, object?> parameters)
        {
            var rendered = await runner.RenderAsync(companyId, "PR75", parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? rendered.Error.Message : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        // RB-1: 1,000 + 400 in shillings; PAYE 140 and union 20 fall 400/1,400 on the shillings (40.00 and 5.71); net 1,240, the 400 in
        // shillings. RB-2: 600, PAYE 60, net 540 of which the 50% split goes in shillings (270). Dollars: gross 1,600, PAYE 160, other 14.29,
        // net 840 + 270 = 1,110. Shillings: gross 400 (1,600,000 USh), net 400 + 270 = 670 (2,680,000 USh).
        string csv = await Render(new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 5, 1), ["ToDate"] = new DateOnly(2026, 5, 31) });
        csv.Should().Contain("RB-05,2026-05,2026-05-25,USD,1,2,\"1,600.00\",\"1,600.00\",160.00,14.29,\"1,110.00\",")
            .And.Contain("RB-05,2026-05,2026-05-25,UGX,0.00025,2,400.00,\"1,600,000.00\",40.00,5.71,\"2,680,000.00\",");
        (await Render(new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 1, 31), ["RunReference"] = "rb-05" }))
            .Should().Contain("RB-05,2026-05,2026-05-25,UGX,", "a run reference picks the run whatever the period");
        (await Render(new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 6, 1), ["ToDate"] = new DateOnly(2026, 6, 30) })).Should().NotContain("RB-05");
    }
}
