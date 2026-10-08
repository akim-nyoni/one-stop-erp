using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers part-period pay and pay frequencies (brief §4.15.4): a mid-month joiner is paid basic for the days employed; a weekly-paid employee is
/// paid their weekly wage in the weekly pay group, taxed on the monthly table's bands scaled to the week.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayFrequencyAndProRataTests
{
    private readonly AccountingFixture _fixture;

    public PayFrequencyAndProRataTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Joiners_are_pro_rated_and_weekly_wages_taxed_on_weekly_bands()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        PayElement basic = new(companyId, "BASIC", "Basic", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        PayElement paye = new(companyId, "PAYE", "PAYE", PayElementType.Deduction, CalculationBasis.TaxTable, 2, "2400");
        db.PayElements.AddRange(basic, paye);
        TaxTable table = new(companyId, "TAX26", new DateOnly(2026, 1, 1));
        table.AddBand(0m, 1000m, 0m);
        table.AddBand(1000m, null, 20m);
        db.Add(table);
        await db.SaveChangesAsync();

        Employee joiner = new(companyId, "PF-NEW", "New Starter", 3100m, new DateOnly(2026, 1, 11));
        Employee waged = new(companyId, "PF-WK", "Weekly Wager", 500m, new DateOnly(2025, 6, 1)) { PayFrequency = PayFrequency.Weekly, PayGroup = "WEEKLY" };
        foreach (Employee e in new[] { joiner, waged })
        {
            e.AssignPayElement(basic.Id);
            e.AssignPayElement(paye.Id);
        }

        db.Employees.AddRange(joiner, waged);
        await db.SaveChangesAsync();
        IPayrollService payroll = scope.ServiceProvider.GetRequiredService<IPayrollService>();

        (await payroll.RunAsync(companyId, "2026-01", new DateOnly(2026, 1, 25), "2420", "PF-2026-01")).IsSuccess.Should().BeTrue();
        Payslip january = await db.Payslips.AsNoTracking().Include(p => p.Lines).SingleAsync(p => p.EmployeeId == joiner.Id);
        january.Lines.Single(l => l.ElementCode == "BASIC").Amount.Should().Be(2100m, "21 of January's 31 days of 3,100");
        january.Lines.Single(l => l.ElementCode == "PAYE").Amount.Should().Be(220m);
        january.Lines.Single(l => l.ElementCode == "BASIC").Workings.Should().Contain("BASIC = 2,100.00").And.Contain("basic 3,100.00 × 21/31 days", "the run keeps its own workings");
        january.Lines.Single(l => l.ElementCode == "PAYE").Workings.Should().Contain("on taxable 2,100.00");
        (await db.Payslips.AnyAsync(p => p.EmployeeId == waged.Id && p.PeriodLabel == "2026-01")).Should().BeFalse("weekly wages are paid in their own run");

        (await payroll.RunGroupAsync(companyId, "WEEKLY", "2026-W02", new DateOnly(2026, 1, 9), "2420", "PF-2026-W02")).IsSuccess.Should().BeTrue();
        Payslip week = await db.Payslips.AsNoTracking().Include(p => p.Lines).SingleAsync(p => p.EmployeeId == waged.Id);
        week.Lines.Single(l => l.ElementCode == "BASIC").Amount.Should().Be(500m);
        week.Lines.Single(l => l.ElementCode == "PAYE").Amount.Should().BeApproximately((500m - (1000m * 12m / 52m)) * 0.20m, 0.02m,
            "the monthly threshold of 1,000 becomes 230.77 a week");
    }
}
