using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Payroll;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers sending a rep's commission to payroll as the period's commission element, rerun safely, and the cheque pay method.</summary>
[Collection(AccountingCollection.Name)]
public sealed class CommissionToPayrollTests
{
    private readonly AccountingFixture _fixture;

    public CommissionToPayrollTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Commission_goes_to_the_reps_payslip()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IPayrollSetupService setup = scope.ServiceProvider.GetRequiredService<IPayrollSetupService>();
        (await setup.SeedStandardElementsAsync(companyId, "6100", "2400", "PAYE")).IsSuccess.Should().BeTrue();
        long employeeId = (await setup.CreateEmployeeAsync(companyId, new NewEmployee("CP-1", "Rep Person", 500m, new DateOnly(2025, 1, 1)) { PaymentMethod = PayMethod.Cheque })).Value;

        SalesRep rep = new(companyId, "CP-REP", "Rep Person", CommissionBasis.Invoiced, 10m) { EmployeeId = employeeId };
        SalesRep outsider = new(companyId, "CP-AGT", "Outside agent", CommissionBasis.Invoiced, 10m);
        db.AddRange(rep, outsider);
        await db.SaveChangesAsync();
        Customer customer = new(companyId, "CP-CUS", "Commissioned customer", Scenario.Usd) { SalesRepId = rep.Id };
        Customer other = new(companyId, "CP-OTH", "Agent's customer", Scenario.Usd) { SalesRepId = outsider.Id };
        db.Customers.AddRange(customer, other);
        await db.SaveChangesAsync();
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        foreach ((Customer c, string number) in new[] { (customer, "CP-INV-1"), (other, "CP-INV-2") })
        {
            (await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = c.Id, DocumentNumber = number, DocumentDate = new DateOnly(2026, 3, 10), Currency = Scenario.Usd, NetAmount = 400m, SalesAccountCode = "4000",
            })).IsSuccess.Should().BeTrue();
        }

        ICommissionService commission = scope.ServiceProvider.GetRequiredService<ICommissionService>();
        (await commission.ToPayrollAsync(companyId, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), " ", "COMMISSION")).Error.Code.Should().Be("Commission.PayrollPeriod");
        IReadOnlyList<CommissionPayrollLine> sent = (await commission.ToPayrollAsync(companyId, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), "2026-03", "COMMISSION")).Value;
        sent.Should().Equal(new CommissionPayrollLine("CP-REP", "CP-1", 40m));
        (await commission.ToPayrollAsync(companyId, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), "2026-03", "COMMISSION")).IsSuccess.Should().BeTrue();

        List<PayrollVariable> variables = await db.PayrollVariables.AsNoTracking().Where(v => v.CompanyId == companyId && v.EmployeeId == employeeId).ToListAsync();
        variables.Should().ContainSingle().Which.Should().Match<PayrollVariable>(v => v.ElementCode == "COMMISSION" && v.PeriodLabel == "2026-03" && v.Amount == 40m);
        (await db.Employees.AsNoTracking().SingleAsync(e => e.Id == employeeId)).PaymentMethod.Should().Be(PayMethod.Cheque);
    }
}
