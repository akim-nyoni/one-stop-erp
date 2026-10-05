using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Payroll;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers multiple payrolls per company: each pay group (and the default payroll) pays only its own employees, the period guard
/// and reversals apply per group, and PR2 totals by pay group.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayGroupTests
{
    private readonly AccountingFixture _fixture;

    public PayGroupTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Pay_groups_run_independently()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        long companyId = await Scenario.SeedAsync(db);
        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        db.PayElements.Add(basic);
        User approver = new($"pg-approver-{companyId}", "Approver");
        db.Users.Add(approver);
        await db.SaveChangesAsync();
        Employee clerk = new(companyId, "PG-1", "Office Clerk", 2000m, new DateOnly(2025, 1, 1));
        Employee hand = new(companyId, "PG-2", "Yard Hand", 300m, new DateOnly(2025, 1, 1)) { PayGroup = "WEEKLY" };
        Employee driver = new(companyId, "PG-3", "Truck Driver", 400m, new DateOnly(2025, 1, 1)) { PayGroup = "Weekly" };
        foreach (Employee e in new[] { clerk, hand, driver })
        {
            e.AssignPayElement(basic.Id);
        }

        db.Employees.AddRange(clerk, hand, driver);
        await db.SaveChangesAsync();

        IPayrollService payroll = scope.ServiceProvider.GetRequiredService<IPayrollService>();
        var monthly = await payroll.RunAsync(companyId, "2026-06", new DateOnly(2026, 6, 25), "2420", "PG-M06");
        monthly.IsSuccess.Should().BeTrue(monthly.IsFailure ? monthly.Error.ToString() : null);
        monthly.Value.PayslipCount.Should().Be(1);
        monthly.Value.TotalNet.Should().Be(2000m);

        // The weekly payroll pays its own two, even under the same period label, and is guarded on its own.
        var weekly = await payroll.RunGroupAsync(companyId, " weekly ", "2026-06", new DateOnly(2026, 6, 26), "2420", "PG-W06");
        weekly.IsSuccess.Should().BeTrue(weekly.IsFailure ? weekly.Error.ToString() : null);
        weekly.Value.PayslipCount.Should().Be(2);
        weekly.Value.TotalNet.Should().Be(700m);
        (await payroll.RunGroupAsync(companyId, "WEEKLY", "2026-06", new DateOnly(2026, 6, 27), "2420", "PG-W06B")).Error.Message.Should().Contain("WEEKLY payroll for period 2026-06");
        (await payroll.RunAsync(companyId, "2026-06", new DateOnly(2026, 6, 27), "2420", "PG-M06B")).Error.Code.Should().Be("Payroll.PeriodAlreadyRun");
        (await payroll.RunGroupAsync(companyId, "CASUAL", "2026-06", new DateOnly(2026, 6, 27), "2420", "PG-C06")).Error.Code.Should().Be("Payroll.NoEmployees");

        // Reversing the weekly run frees that group's period only.
        (await scope.ServiceProvider.GetRequiredService<IPayrollAdjustmentService>().ReverseRunAsync(companyId, "PG-W06", new DateOnly(2026, 6, 28), "Wrong week", approver.Id)).IsSuccess.Should().BeTrue();
        (await payroll.RunAsync(companyId, "2026-06", new DateOnly(2026, 6, 28), "2420", "PG-M06C")).Error.Code.Should().Be("Payroll.PeriodAlreadyRun");
        (await payroll.RunGroupAsync(companyId, "WEEKLY", "2026-06", new DateOnly(2026, 6, 29), "2420", "PG-W06C")).IsSuccess.Should().BeTrue();
        (await db.Set<PayrollRunRecord>().AsNoTracking().Where(r => r.CompanyId == companyId).Select(r => r.PayGroup).ToListAsync()).Should().BeEquivalentTo(["", "WEEKLY", "WEEKLY", "WEEKLY"]);

        var rendered = await scope.ServiceProvider.GetRequiredService<IReportRunner>().RenderAsync(companyId, "PR2",
            new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 6, 1), ["ToDate"] = new DateOnly(2026, 6, 30) }, ReportFormat.Csv);
        rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? rendered.Error.Message : null);
        string csv = Encoding.UTF8.GetString(rendered.Value.Content);
        csv.Should().Contain("Default,Earning,BASIC,Basic Salary,1,2000").And.Contain("WEEKLY,Earning,BASIC,Basic Salary,2,700");
    }
}
