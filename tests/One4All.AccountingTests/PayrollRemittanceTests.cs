using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Payroll;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers third-party payments: a committed run raises AP invoices for the tax authority, the pension scheme (employee and employer
/// parts together) and a garnishee creditor, clearing the payroll liabilities into AP; once only per run; on demand when the company
/// does not raise them automatically; and PR73 lists them.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayrollRemittanceTests
{
    private readonly AccountingFixture _fixture;

    public PayrollRemittanceTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Runs_raise_supplier_invoices_for_their_third_parties()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        long companyId = await Scenario.SeedAsync(db);
        Company company = await db.Companies.SingleAsync(c => c.Id == companyId);
        company.RaisePayrollRemittances = true;
        Supplier taxman = new(companyId, "RM-TAX", "Revenue Authority", Scenario.Usd), fund = new(companyId, "RM-PEN", "Pension Fund", Scenario.Usd), creditor = new(companyId, "RM-CRED", "Furniture Store", Scenario.Usd);
        db.Suppliers.AddRange(taxman, fund, creditor);
        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        PayElement paye = new(companyId, "PAYE", "PAYE", PayElementType.Deduction, CalculationBasis.Formula, 2, "2400") { Formula = "TAXABLE * 10 / 100" };
        PayElement pension = new(companyId, "PENSION", "Pension", PayElementType.Deduction, CalculationBasis.PercentOfBasic, 3, "2410") { Rate = 5m };
        PayElement employer = new(companyId, "PENSION-ER", "Pension (employer)", PayElementType.EmployerContribution, CalculationBasis.PercentOfBasic, 4, "2410")
        { Rate = 5m, ContributionExpenseAccountCode = "6100" };
        PayElement court = new(companyId, "GARN", "Court order", PayElementType.Deduction, CalculationBasis.FixedAmount, 5, "2410") { FixedAmount = 100m };
        db.PayElements.AddRange(basic, paye, pension, employer, court);
        await db.SaveChangesAsync();
        db.Add(new StatutoryScheme(companyId, "PAYE", "Income tax", SchemeKind.TaxAuthority, "Revenue Authority", "PAYE", null) { SupplierId = taxman.Id });
        db.Add(new StatutoryScheme(companyId, "PENSION", "Staff pension", SchemeKind.Pension, "Pension Fund", "PENSION", "PENSION-ER") { SupplierId = fund.Id });
        Employee ada = new(companyId, "RM-1", "Ada One", 1000m, new DateOnly(2025, 1, 1)), bob = new(companyId, "RM-2", "Bob Two", 2000m, new DateOnly(2025, 1, 1));
        foreach (Employee e in new[] { ada, bob })
        {
            e.AssignPayElement(basic.Id);
            e.AssignPayElement(paye.Id);
            e.AssignPayElement(pension.Id);
            e.AssignPayElement(employer.Id);
        }

        ada.AssignPayElement(court.Id);
        db.Employees.AddRange(ada, bob);
        await db.SaveChangesAsync();
        GarnisheeOrder order = new(companyId, ada.Id, "GO-1", "Furniture Store", 1, 100m, 1000m, "GARN", new DateOnly(2026, 1, 1)) { SupplierId = creditor.Id };
        db.Add(order);
        await db.SaveChangesAsync();

        // September: PAYE 300, pension 150 + 150 employer, court order 100 - three supplier invoices with the run.
        IPayrollService payroll = scope.ServiceProvider.GetRequiredService<IPayrollService>();
        var september = await payroll.RunAsync(companyId, "2026-09", new DateOnly(2026, 9, 25), "2420", "RM-09");
        september.IsSuccess.Should().BeTrue(september.IsFailure ? september.Error.ToString() : null);
        (await db.RemittanceInvoices.AsNoTracking().Where(r => r.CompanyId == companyId).OrderBy(r => r.DocumentNumber).Select(r => new { r.DocumentNumber, r.AccountCode, r.Amount }).ToListAsync())
            .Should().Equal(new { DocumentNumber = "RM-09-GO-1", AccountCode = "2410", Amount = 100m }, new { DocumentNumber = "RM-09-PAYE", AccountCode = "2400", Amount = 300m },
                new { DocumentNumber = "RM-09-PENSION", AccountCode = "2410", Amount = 300m });
        IPayrollRemittanceService remittances = scope.ServiceProvider.GetRequiredService<IPayrollRemittanceService>();
        (await remittances.RaiseRemittancesAsync(companyId, "RM-09")).Error.Code.Should().Be("Payroll.RemittancesRaised");
        (await db.OpenItems.CountAsync(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsPayable)).Should().Be(3);

        // October without the automatic step: nothing raised until asked.
        company.RaisePayrollRemittances = false;
        await db.SaveChangesAsync();
        (await payroll.RunAsync(companyId, "2026-10", new DateOnly(2026, 10, 25), "2420", "RM-10")).IsSuccess.Should().BeTrue();
        (await db.RemittanceInvoices.CountAsync(r => r.CompanyId == companyId && r.RunReference == "RM-10")).Should().Be(0);
        (await remittances.RaiseRemittancesAsync(companyId, "RM-10")).Value.Select(r => r.Payee).Should().Equal("PAYE", "PENSION", "GO-1");

        // The payroll liabilities clear into AP.
        TrialBalanceReport tb = await scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>().TrialBalanceAsync(companyId, new DateOnly(2026, 10, 31));
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Where(r => r.AccountCode is "2400" or "2410").Sum(r => r.Debit - r.Credit).Should().Be(0m);

        var rendered = await scope.ServiceProvider.GetRequiredService<IReportRunner>().RenderAsync(companyId, "PR73",
            new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 9, 1), ["ToDate"] = new DateOnly(2026, 9, 30) }, ReportFormat.Csv);
        rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? rendered.Error.Message : null);
        Encoding.UTF8.GetString(rendered.Value.Content).Should().Contain("RM-09,2026-09,PAYE,RM-TAX,2400,RM-09-PAYE,300.00,300")
            .And.Contain("RM-09,2026-09,GO-1,RM-CRED,2410,RM-09-GO-1,100.00,100").And.NotContain("RM-10");
    }
}
