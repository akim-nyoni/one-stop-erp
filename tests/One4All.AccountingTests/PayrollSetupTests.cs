using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Accounting;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers payroll set-up without code: engaging and updating employees (unique numbers, a contract needs its end date), the standard pay
/// elements, a tax table checked band by band, assigning elements, a payroll run using them all, and bank-detail changes that only a
/// second person can approve.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayrollSetupTests
{
    private readonly AccountingFixture _fixture;

    public PayrollSetupTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Payroll_is_set_up_and_runs()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IPayrollSetupService setup = scope.ServiceProvider.GetRequiredService<IPayrollSetupService>();

        // Employees.
        (await setup.CreateEmployeeAsync(companyId, new NewEmployee("ps-001", "Chipo Moyo", 500m, new DateOnly(2025, 6, 1))
        {
            Department = "FIN", Grade = "C2", NationalIdNumber = "63-123456-A-42", BankName = "CBZ", BankAccountNumber = "0011223344",
        })).IsSuccess.Should().BeTrue();
        (await setup.CreateEmployeeAsync(companyId, new NewEmployee("PS-001", "Duplicate", 100m, new DateOnly(2025, 6, 1)))).Error.Code.Should().Be("Payroll.EmployeeExists");
        (await setup.CreateEmployeeAsync(companyId, new NewEmployee("PS-002", "Contract worker", 100m, new DateOnly(2025, 6, 1)) { EmploymentType = EmploymentType.Contract }))
            .Error.Code.Should().Be("Payroll.ContractEnd");
        (await setup.UpdateEmployeeAsync(companyId, "PS-001", new EmployeeUpdate { Grade = "C3", BasicSalary = 600m })).IsSuccess.Should().BeTrue();
        (await setup.UpdateEmployeeAsync(companyId, "NOPE", new EmployeeUpdate())).Error.Code.Should().Be("Payroll.EmployeeNotFound");
        Employee chipo = await db.Employees.AsNoTracking().SingleAsync(e => e.CompanyId == companyId && e.EmployeeNumber == "PS-001");
        (chipo.Grade, chipo.BasicSalary, chipo.Department, chipo.BankAccountNumber).Should().Be(("C3", 600m, "FIN", "0011223344"));
        (await setup.UpdateEmployeeAsync(companyId, "PS-001", new EmployeeUpdate { Department = "Finance" })).Error.Code.Should().Be("Payroll.DimensionCode");

        // Tax table: bands must follow on.
        (await setup.CreateTaxTableAsync(companyId, "PAYE", new DateOnly(2026, 1, 1), null, [new TaxBandInput(0m, 100m, 0m), new TaxBandInput(150m, null, 20m)]))
            .Error.Code.Should().Be("Payroll.TaxBands");
        (await setup.CreateTaxTableAsync(companyId, "PAYE", new DateOnly(2026, 1, 1), null, [new TaxBandInput(0m, 100m, 0m), new TaxBandInput(100m, 300m, 20m)]))
            .Error.Code.Should().Be("Payroll.TaxBands");
        (await setup.CreateTaxTableAsync(companyId, "PAYE", new DateOnly(2026, 1, 1), null,
            [new TaxBandInput(0m, 100m, 0m), new TaxBandInput(100m, 300m, 20m), new TaxBandInput(300m, null, 30m)])).IsSuccess.Should().BeTrue();

        // The standard elements, once.
        IReadOnlyList<string> seeded = (await setup.SeedStandardElementsAsync(companyId, "6100", "2400", "PAYE")).Value;
        seeded.Should().HaveCount(32).And.StartWith(["BASIC", "OVERTIME", "HOUSING", "TRANSPORT", "PAYE"]).And.Contain(["OVERTIME2", "OVERTIMEPH", "GRATUITY", "GARNISHEE", "STAFFPURCH", "HOUSERENT"]);
        (await setup.SeedStandardElementsAsync(companyId, "6100", "2400", "PAYE")).Value.Should().BeEmpty();
        (await setup.CreatePayElementAsync(companyId, new NewPayElement("BAD", "Bad", PayElementType.Earning, CalculationBasis.Formula, 9, "6100"))).Error.Code.Should().Be("Payroll.FormulaRequired");
        (await setup.UpdatePayElementAsync(companyId, "HOUSING", 0m, null, null, null)).IsSuccess.Should().BeTrue();

        // The seeded recoveries recover (so what was deducted before is netted off), the allowances report as benefits, and options can be set later.
        Dictionary<string, PayElement> elements = await db.PayElements.AsNoTracking().Where(p => p.CompanyId == companyId).ToDictionaryAsync(p => p.Code);
        (elements["STAFFPURCH"].Recovers, elements["HOUSERENT"].Recovers, elements["HOUSING"].BenefitCategory).Should().Be((PayRecovery.StaffPurchases, PayRecovery.HousingRent, "Housing"));
        (await setup.SetPayElementOptionsAsync(companyId, "HOUSING", new PayElementOptions(PayRecovery.HousingRent))).Error.Code.Should().Be("Payroll.RecoveryType");
        (await setup.SetPayElementOptionsAsync(companyId, "PAYE", new PayElementOptions(PaidInCurrency: "XX"))).Error.Code.Should().Be("Payroll.PaidInCurrency");
        (await setup.SetPayElementOptionsAsync(companyId, "PAYE", new PayElementOptions(CumulativeTax: true, SplitCurrencyTax: true))).IsSuccess.Should().BeTrue();
        (await setup.SetPayElementOptionsAsync(companyId, "TRANSPORT", new PayElementOptions(BenefitCategory: "Travel", PaidInCurrency: "eur", IsAnnualPayment: true))).IsSuccess.Should().BeTrue();
        PayElement paye = await db.PayElements.AsNoTracking().SingleAsync(p => p.CompanyId == companyId && p.Code == "PAYE");
        (paye.CumulativeTax, paye.SplitCurrencyTax).Should().Be((true, true));
        PayElement transport = await db.PayElements.AsNoTracking().SingleAsync(p => p.CompanyId == companyId && p.Code == "TRANSPORT");
        (transport.BenefitCategory, transport.PaidInCurrency?.Value, transport.IsAnnualPayment).Should().Be(("Travel", "EUR", true));
        (await setup.SetPayElementOptionsAsync(companyId, "PAYE", new PayElementOptions())).IsSuccess.Should().BeTrue("the run below expects the standard options");
        (await setup.SetPayElementOptionsAsync(companyId, "TRANSPORT", new PayElementOptions(BenefitCategory: "Transport"))).IsSuccess.Should().BeTrue();
        (await setup.CreateTaxTableAsync(companyId, "PAYE-EUR", new DateOnly(2026, 1, 1), CurrencyCode.Parse("EUR"), [new TaxBandInput(0m, 50m, 0m), new TaxBandInput(50m, null, 25m)])).IsSuccess.Should().BeTrue();

        (await setup.AssignElementAsync(companyId, "PS-001", "BASIC")).IsSuccess.Should().BeTrue();
        (await setup.AssignElementAsync(companyId, "PS-001", "HOUSING", overrideAmount: 100m)).IsSuccess.Should().BeTrue();
        (await setup.AssignElementAsync(companyId, "PS-001", "PAYE")).IsSuccess.Should().BeTrue();
        (await setup.AssignElementAsync(companyId, "PS-001", "BASIC")).Error.Code.Should().Be("Payroll.ElementAssigned");

        // The run: gross 700; tax 0 + 40 + 120 = 160; net 540.
        var run = await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-03", new DateOnly(2026, 3, 25), "2420", "PS-2026-03");
        run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.ToString() : null);
        Payslip slip = await db.Payslips.AsNoTracking().SingleAsync(p => p.CompanyId == companyId && p.EmployeeId == chipo.Id);
        (slip.GrossEarnings, slip.TotalDeductions, slip.NetPay).Should().Be((700m, 160m, 540m));
        (await setup.EndElementAsync(companyId, "PS-001", "HOUSING")).IsSuccess.Should().BeTrue();
        (await setup.EndElementAsync(companyId, "PS-001", "HOUSING")).Error.Code.Should().Be("Payroll.ElementNotAssigned");

        // Bank details: requested by one person, approved by another.
        IReadOnlyList<long> requests = (await setup.RequestBankChangeAsync(companyId, "PS-001", new Dictionary<string, string> { ["BankAccountNumber"] = "9988776655" }, requestedByUserId: 41)).Value;
        (await setup.RequestBankChangeAsync(companyId, "PS-001", new Dictionary<string, string> { ["BasicSalary"] = "9999" }, 41)).Error.Code.Should().Be("Payroll.BankFields");
        (await setup.PendingBankChangesAsync(companyId)).Should().ContainSingle(r => r.RequestId == requests[0] && r.OldValue == "0011223344" && r.NewValue == "9988776655");
        (await setup.DecideBankChangeAsync(companyId, requests[0], true, 41, null)).Error.Code.Should().Be("Payroll.SelfApproval");
        (await db.Employees.AsNoTracking().SingleAsync(e => e.Id == chipo.Id)).BankAccountNumber.Should().Be("0011223344");
        (await setup.DecideBankChangeAsync(companyId, requests[0], true, 42, "Bank letter seen")).IsSuccess.Should().BeTrue();
        (await db.Employees.AsNoTracking().SingleAsync(e => e.Id == chipo.Id)).BankAccountNumber.Should().Be("9988776655");
        (await setup.DecideBankChangeAsync(companyId, requests[0], true, 42, null)).Error.Code.Should().Be("Payroll.ChangeNotPending");
    }
}
