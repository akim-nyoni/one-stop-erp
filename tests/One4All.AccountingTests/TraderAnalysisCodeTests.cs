using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Onboarding;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the transaction analysis codes on customer and supplier accounts (brief §4.5): they come in with the take-on file, upper-cased, and the
/// registers list them.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class TraderAnalysisCodeTests
{
    private readonly AccountingFixture _fixture;

    public TraderAnalysisCodeTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Analysis_codes_are_imported_and_listed()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IOpeningBalanceService imports = scope.ServiceProvider.GetRequiredService<IOpeningBalanceService>();

        (await imports.ImportCustomersAsync(companyId, "code,name,currency,credit_limit,analysis1,analysis2\nAC-1,Analysed Ltd,USD,500,north,retail\nAC-2,Plain Ltd,USD,0,,\n", commit: true)).Created.Should().Be(2);
        (await imports.ImportSuppliersAsync(companyId, "code,name,currency,analysis3\nAS-1,Supplied Co,USD,imports\n", commit: true)).Created.Should().Be(1);
        Customer analysed = await db.Customers.AsNoTracking().SingleAsync(c => c.CompanyId == companyId && c.Code == "AC-1");
        (analysed.AnalysisCode1, analysed.AnalysisCode2, analysed.AnalysisCode3).Should().Be(("NORTH", "RETAIL", null));
        (await db.Customers.AsNoTracking().SingleAsync(c => c.CompanyId == companyId && c.Code == "AC-2")).AnalysisCode1.Should().BeNull();
        (await db.Suppliers.AsNoTracking().SingleAsync(s => s.CompanyId == companyId && s.Code == "AS-1")).AnalysisCode3.Should().Be("IMPORTS");

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        var customers = await runner.RenderAsync(companyId, One4All.Reporting.SystemReports.CustomerRegisterCode, new Dictionary<string, object?>(), ReportFormat.Csv);
        customers.IsSuccess.Should().BeTrue(customers.IsFailure ? customers.Error.Message : null);
        System.Text.Encoding.UTF8.GetString(customers.Value.Content).Should().Contain("AC-1,Analysed Ltd").And.Contain("NORTH,RETAIL");
        var suppliers = await runner.RenderAsync(companyId, One4All.Reporting.SystemReports.SupplierRegisterCode, new Dictionary<string, object?>(), ReportFormat.Csv);
        System.Text.Encoding.UTF8.GetString(suppliers.Value.Content).Should().Contain("AS-1,Supplied Co").And.Contain("IMPORTS");
    }
}
