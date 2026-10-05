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
/// Covers the multi-currency tax split (PR35): earnings paid in a second currency are converted at the pay date's rate, and the tax is
/// apportioned from the base table by share — or computed on the currency's own table once one is in force.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayrollCurrencySplitTests
{
    private static readonly CurrencyCode Mwk = CurrencyCode.Parse("MWK");
    private readonly AccountingFixture _fixture;

    public PayrollCurrencySplitTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Tax_is_split_across_the_currencies_earnings_are_paid_in()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        long companyId = await Scenario.SeedAsync(db);
        DateOnly rateDate = new(2026, 5, 1);
        if (!await db.ExchangeRates.AnyAsync(r => r.FromCurrency == Scenario.Usd && r.ToCurrency == Mwk && r.EffectiveFrom == rateDate))
        {
            db.ExchangeRates.Add(new ExchangeRate(Scenario.Usd, Mwk, ExchangeRateType.Daily, rateDate, 2000m));
        }

        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        PayElement local = new(companyId, "LOCAL", "Local-currency allowance", PayElementType.Earning, CalculationBasis.FixedAmount, 2, "6100") { FixedAmount = 500m, Taxable = true, PaidInCurrency = Mwk };
        PayElement paye = new(companyId, "PAYE", "PAYE", PayElementType.Deduction, CalculationBasis.Formula, 3, "2400") { Formula = "TAXABLE * 10 / 100" };
        db.PayElements.AddRange(basic, local, paye);
        TaxTable table = new(companyId, "PAYE26", new DateOnly(2026, 1, 1));
        table.AddBand(0m, 500m, 0m);
        table.AddBand(500m, null, 20m);
        db.Add(table);
        await db.SaveChangesAsync();
        Employee split = new(companyId, "PX-1", "Pat Split", 1000m, new DateOnly(2025, 1, 1));
        Employee single = new(companyId, "PX-2", "Una Single", 900m, new DateOnly(2025, 1, 1));
        split.AssignPayElement(basic.Id);
        split.AssignPayElement(local.Id);
        split.AssignPayElement(paye.Id);
        single.AssignPayElement(basic.Id);
        single.AssignPayElement(paye.Id);
        db.Employees.AddRange(split, single);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-05", new DateOnly(2026, 5, 25), "2420", "PX-05")).IsSuccess.Should().BeTrue();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render()
        {
            var rendered = await runner.RenderAsync(companyId, "PR35", new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 5, 1), ["ToDate"] = new DateOnly(2026, 5, 31) }, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? rendered.Error.Message : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        // Base table on the whole 1,500 (tax 200), apportioned two-thirds / one-third; the deduction (150) the same way.
        (await Render()).Should().Contain("PX-1 Pat Split,2026-05,USD,\"1,000.00\",1,\"1,000.00\",66.7%,\"Apportioned, PAYE26 20% from 500.00\",133.33,100.00,133.33")
            .And.Contain("PX-1 Pat Split,2026-05,MWK,\"1,000,000.00\",0.0005,500.00,33.3%,\"Apportioned, PAYE26 20% from 500.00\",\"133,340.00\",50.00,66.67")
            .And.NotContain("Una Single");

        // Once the kwacha has its own table, that portion is taxed on it in kwacha (25% over 500,000 = 125,000 = 62.50 base).
        TaxTable kwacha = new(companyId, "PAYE26MWK", new DateOnly(2026, 1, 1), Mwk);
        kwacha.AddBand(0m, 500_000m, 0m);
        kwacha.AddBand(500_000m, null, 25m);
        db.Add(kwacha);
        await db.SaveChangesAsync();
        string own = await Render();
        own.Should().Contain("PX-1 Pat Split,2026-05,MWK,\"1,000,000.00\",0.0005,500.00,33.3%,\"Own table, PAYE26MWK 25% from 500,000.00\",\"125,000.00\",50.00,62.5");
        own.Should().Contain("PX-1 Pat Split,2026-05,USD,\"1,000.00\",1,\"1,000.00\",66.7%,\"Apportioned, PAYE26 20% from 500.00\",133.33");
    }
}
