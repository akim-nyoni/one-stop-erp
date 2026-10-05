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
/// Covers variable input: keyed and imported variables (units for a rate-per-unit element, an amount for a once-off element the
/// employee does not normally have), the import preview and its errors, the run using only the period's variables, and PR72.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayrollVariableTests
{
    private readonly AccountingFixture _fixture;

    public PayrollVariableTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Variables_are_keyed_or_imported_and_paid_in_their_period()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        long companyId = await Scenario.SeedAsync(db);
        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        PayElement overtime = new(companyId, "OT", "Overtime", PayElementType.Earning, CalculationBasis.RatePerUnit, 2, "6100") { Rate = 15m, Taxable = true };
        PayElement bonus = new(companyId, "BONUS", "Once-off bonus", PayElementType.Earning, CalculationBasis.FixedAmount, 3, "6100") { Taxable = true };
        PayElement paye = new(companyId, "PAYE", "PAYE", PayElementType.Deduction, CalculationBasis.Formula, 4, "2400") { Formula = "TAXABLE * 10 / 100" };
        db.PayElements.AddRange(basic, overtime, bonus, paye);
        await db.SaveChangesAsync();
        Employee val = new(companyId, "PV-1", "Val One", 1000m, new DateOnly(2025, 1, 1));
        Employee vic = new(companyId, "PV-2", "Vic Two", 2000m, new DateOnly(2025, 1, 1));
        Employee gone = new(companyId, "PV-3", "Gone Three", 900m, new DateOnly(2025, 1, 1));
        val.AssignPayElement(basic.Id);
        val.AssignPayElement(overtime.Id);
        val.AssignPayElement(paye.Id);
        vic.AssignPayElement(basic.Id);
        vic.AssignPayElement(paye.Id);
        gone.Terminate();
        db.Employees.AddRange(val, vic, gone);
        await db.SaveChangesAsync();

        IPayrollVariableService variables = scope.ServiceProvider.GetRequiredService<IPayrollVariableService>();
        (await variables.SetAsync(companyId, val.Id, "OT", "2026-07", 10m, null)).IsSuccess.Should().BeTrue();
        (await variables.SetAsync(companyId, val.Id, "BONUS", "2026-07", 3m, null)).Error.Message.Should().Contain("not paid per unit");
        (await variables.SetAsync(companyId, val.Id, "OT", "2026-07", 1m, 5m)).Error.Message.Should().Contain("one of them");

        // The preview lists every line with its error; an import with any error is refused whole.
        const string bad = "Employee,Element,Units,Amount\nPV-2,OT,4,\nPV-2,BONUS,,250\nPV-9,OT,1,\nPV-1,XYZ,,5\nPV-2,OT,2,\nPV-3,BONUS,,10\n";
        VariableImportPreview preview = (await variables.PreviewImportAsync(companyId, "2026-07", bad)).Value;
        preview.Lines.Should().HaveCount(6);
        preview.ErrorCount.Should().Be(4);
        preview.Lines.Select(l => l.Error).Should().Equal(null, null, "No employee 'PV-9'.", "No pay element 'XYZ'.", "Duplicate line for this employee and element.", "PV-3 is not active.");
        (await variables.ImportAsync(companyId, "2026-07", bad)).Error.Code.Should().Be("Payroll.VariableErrors");
        (await variables.ImportAsync(companyId, "2026-07", "Employee,Element,Units,Amount\nPV-2,OT,4,\nPV-2,BONUS,,250\n")).Value.Should().Be(2);

        // July pays the variables (Vic's overtime and bonus are once-offs); August has none.
        IPayrollService payroll = scope.ServiceProvider.GetRequiredService<IPayrollService>();
        (await payroll.RunAsync(companyId, "2026-07", new DateOnly(2026, 7, 25), "2420", "PV-07")).Value.TotalNet.Should().Be((1150m * 0.9m) + (2310m * 0.9m));
        (await payroll.RunAsync(companyId, "2026-08", new DateOnly(2026, 8, 25), "2420", "PV-08")).Value.TotalNet.Should().Be(2700m);
        (await variables.SetAsync(companyId, val.Id, "OT", "2026-07", 2m, null)).Error.Message.Should().Contain("already been paid");

        var rendered = await scope.ServiceProvider.GetRequiredService<IReportRunner>().RenderAsync(companyId, "PR72",
            new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 7, 1), ["ToDate"] = new DateOnly(2026, 7, 31) }, ReportFormat.Csv);
        rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? rendered.Error.Message : null);
        string csv = Encoding.UTF8.GetString(rendered.Value.Content);
        csv.Should().Contain("2026-07,PV-1 Val One,OT,10,-,MANUAL,Paid,150")
            .And.Contain("2026-07,PV-2 Vic Two,BONUS,-,250.00,IMPORT,Paid,250")
            .And.Contain("2026-07,PV-2 Vic Two,OT,4,-,IMPORT,Paid,60");
    }
}
