using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Security;
using One4All.Core.Reporting;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers report security through the audited runner: a payroll report needs the payroll reports right on top of the general one, and a
/// user scoped to a department sees only that department's rows while an unscoped colleague sees them all.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ReportSecurityTests
{
    private readonly AccountingFixture _fixture;

    public ReportSecurityTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Reports_respect_module_rights_and_scopes()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        IPayrollSetupService setup = scope.ServiceProvider.GetRequiredService<IPayrollSetupService>();
        (await setup.CreateEmployeeAsync(companyId, new NewEmployee("RS-FIN", "Finance Clerk", 500m, new DateOnly(2025, 1, 1)) { Department = "FIN" })).IsSuccess.Should().BeTrue();
        (await setup.CreateEmployeeAsync(companyId, new NewEmployee("RS-WKS", "Works Labourer", 400m, new DateOnly(2025, 1, 1)) { Department = "WKS" })).IsSuccess.Should().BeTrue();

        IUserAdminService admin = scope.ServiceProvider.GetRequiredService<IUserAdminService>();
        string tag = Guid.NewGuid().ToString("N")[..8];
        long Role(string name, params string[] rights)
        {
            long id = admin.CreateRoleAsync($"{name} {tag}", null).GetAwaiter().GetResult().Value;
            Role role = db.Roles.Single(r => r.Id == id);
            foreach (string right in rights)
            {
                role.Grant(right, OperationFlags.View | OperationFlags.Export);
            }

            db.SaveChanges();
            return id;
        }

        long readers = Role("Readers", "Reporting.Report"), payroll = Role("Payroll readers", "Reporting.Report", "Payroll.Reports");
        long clerk = (await admin.CreateUserAsync($"clerk.{tag}", "Clerk", null, "Reports2026x")).Value;
        long financeHr = (await admin.CreateUserAsync($"finhr.{tag}", "Finance HR", null, "Reports2026x")).Value;
        long headHr = (await admin.CreateUserAsync($"headhr.{tag}", "Head of HR", null, "Reports2026x")).Value;
        (await admin.AssignRoleAsync(clerk, readers, 900_001, new Dictionary<ScopeDimension, IReadOnlyList<string>>())).IsSuccess.Should().BeTrue();
        (await admin.AssignRoleAsync(financeHr, payroll, 900_001, new Dictionary<ScopeDimension, IReadOnlyList<string>> { [ScopeDimension.Department] = ["FIN"] })).IsSuccess.Should().BeTrue();
        (await admin.AssignRoleAsync(headHr, payroll, 900_001, new Dictionary<ScopeDimension, IReadOnlyList<string>>())).IsSuccess.Should().BeTrue();

        IAuditedReportRunner reports = scope.ServiceProvider.GetRequiredService<IAuditedReportRunner>();
        Dictionary<string, object?> parameters = new() { ["AsAtDate"] = new DateOnly(2026, 6, 30) };
        async Task<string> Run(long user) => Encoding.UTF8.GetString((await reports.RunAsync(companyId, "PR4", parameters, ReportFormat.Csv, ReportRunPurpose.View, user)).Value.Document.Content);

        (await reports.RunAsync(companyId, "PR4", parameters, ReportFormat.Csv, ReportRunPurpose.View, clerk)).Error.Code.Should().Be("Rpt.ModuleNotAuthorised");
        string scoped = await Run(financeHr);
        scoped.Should().Contain("Finance Clerk").And.NotContain("Works Labourer");
        (await Run(headHr)).Should().Contain("Finance Clerk").And.Contain("Works Labourer");
    }
}
