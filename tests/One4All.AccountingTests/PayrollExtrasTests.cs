using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers split pay, benefits in kind, terminal benefits, labour mix and reprints (brief §14 O PR14, PR34, PR52, PR54, PR62, PR69).
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayrollExtrasTests
{
    private readonly AccountingFixture _fixture;

    public PayrollExtrasTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Payroll_extras_are_reported()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        PayElement car = new(companyId, "CAR", "Company car", PayElementType.Earning, CalculationBasis.FixedAmount, 2, "6100") { FixedAmount = 300m, Taxable = true, BenefitCategory = "Motor vehicle" };
        db.PayElements.AddRange(basic, car);
        TaxTable table = new(companyId, "TAXX", new DateOnly(2026, 1, 1));
        table.AddBand(0m, 1000m, 0m);
        table.AddBand(1000m, null, 20m);
        db.Add(table);
        await db.SaveChangesAsync();
        Employee ann = new(companyId, "XA-A", "Ann", 2_600m, new DateOnly(2020, 1, 1)) { Department = "Ops", DateOfBirth = new DateOnly(1966, 4, 1) };
        Employee cas = new(companyId, "XA-C", "Cas", 1_000m, new DateOnly(2025, 1, 1)) { Department = "Ops", EmploymentType = EmploymentType.Casual };
        Employee ted = new(companyId, "XA-T", "Ted", 2_600m, new DateOnly(2020, 1, 1)) { Department = "Ops" };
        ann.AssignPayElement(basic.Id);
        ann.AssignPayElement(car.Id);
        cas.AssignPayElement(basic.Id);
        ted.AssignPayElement(basic.Id);
        db.Employees.AddRange(ann, cas, ted);
        await db.SaveChangesAsync();
        db.AddRange(new EmployeeLoan(companyId, ted.Id, "LN-T", 500m, 100m, new DateOnly(2025, 6, 1), "1400"), new EmployeePaymentSplit(companyId, ann.Id, PayMethod.MobileMoney, "0772", 500m));
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<ILeaveService>().AccrueAsync(companyId, ted.Id, "Annual", 10m)).IsSuccess.Should().BeTrue();

        IPayrollService payroll = scope.ServiceProvider.GetRequiredService<IPayrollService>();
        (await payroll.RunAsync(companyId, "2026-03", new DateOnly(2026, 3, 25), "2420", "XR-03")).IsSuccess.Should().BeTrue();
        ted.Terminate(new DateOnly(2026, 3, 31), "Retrenchment");
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IAttendanceService>().RecordDayAsync(companyId, ann.Id, new DateOnly(2026, 3, 10), AttendanceStatus.Present, new TimeOnly(8, 0), new TimeOnly(19, 0), 0, 2m, 1.5m))
            .IsSuccess.Should().BeTrue();
        (await payroll.RenderPayslipTextAsync(companyId, ann.Id, "2026-03", new DateOnly(2026, 3, 25))).IsSuccess.Should().BeTrue();
        (await payroll.RenderPayslipTextAsync(companyId, ann.Id, "2026-03", new DateOnly(2026, 3, 25))).IsSuccess.Should().BeTrue();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code, DateOnly? from = null, DateOnly? to = null)
        {
            Dictionary<string, object?> parameters = new()
            {
                ["FromDate"] = from ?? new DateOnly(2026, 3, 1), ["ToDate"] = to ?? new DateOnly(2026, 3, 31), ["AsAtDate"] = new DateOnly(2026, 3, 31),
                ["NoticeMonths"] = 1m, ["GratuityWeeksPerYear"] = 2m, ["SeveranceWeeksPerYear"] = 4m, ["TerminalTaxRate"] = 10m, ["WeeksPerYear"] = 2m, ["RetirementAge"] = 65, ["DiscountRate"] = 10m,
            };
            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("PR14")).Should().Contain("XA-A Ann,BankTransfer —,MobileMoney 0772 500.00,\"2,900.00\",2400");
        (await Render("PR34")).Should().Contain("XA-A Ann,Motor vehicle,Company car,300.00,60");
        (await Render("PR52")).Should().Contain("XA-T Ted,2026-03-31,Retrenchment,6,\"2,600.00\",\"1,199.82\",\"7,200.00\",\"14,400.00\",500.00,\"2,160.00\",22739.82");
        (await Render("PR54")).Should().Contain("XA-A Ann,6,\"7,200.00\",6,4064.21").And.Contain("XA-C Cas,1,461.54,—,461.54");
        (await Render("PR62")).Should().Contain("Ops,\"5,500.00\",\"1,000.00\",45.00,19.0%,6545");
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        string reprints = await Render("PR69", today.AddDays(-1), today.AddDays(1));
        reprints.Should().Contain("XA-A Ann,2026-03,2,").And.NotContain(",2026-03,1,");
    }
}
