using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Security;
using One4All.Core.Payroll;
using One4All.Core.Security;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the payroll walled garden in the audit trail: changes to employees are left out of what an auditor without the payroll audit
/// right sees (other changes still show), and are shown to a payroll auditor and to the system.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayrollWalledGardenTests
{
    private readonly AccountingFixture _fixture;

    public PayrollWalledGardenTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Payroll_changes_are_kept_from_auditors_without_the_payroll_audit_right()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Role general = new($"General auditor {companyId}"), payrollAudit = new($"Payroll auditor {companyId}");
        general.Grant("System.Audit", OperationFlags.View);
        payrollAudit.Grant("System.Audit", OperationFlags.View);
        payrollAudit.Grant("Payroll.Audit", OperationFlags.View);
        db.Roles.AddRange(general, payrollAudit);
        User auditor = new($"wg-aud-{companyId}", "Auditor"), payrollAuditor = new($"wg-pay-{companyId}", "Payroll Auditor");
        db.Users.AddRange(auditor, payrollAuditor);
        db.Employees.Add(new Employee(companyId, "WG-E1", "Confidential Person", 9_000m, new DateOnly(2025, 1, 1)));
        db.Customers.Add(new Customer(companyId, "WG-CUS", "Open Customer", Scenario.Usd));
        await db.SaveChangesAsync();
        auditor.AssignRole(general.Id);
        payrollAuditor.AssignRole(payrollAudit.Id);
        await db.SaveChangesAsync();

        IAuditService audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
        async Task<IReadOnlyList<string>> Types(long? requester) => [.. (await audit.QueryAsync(new AuditQuery { CompanyId = companyId, Take = 5_000, RequestingUserId = requester }))
            .Select(e => e.EntityType).Distinct()];
        (await Types(auditor.Id)).Should().Contain("Customer").And.NotContain("Employee");
        (await Types(payrollAuditor.Id)).Should().Contain(["Customer", "Employee"]);
        (await Types(null)).Should().Contain("Employee");
    }
}
