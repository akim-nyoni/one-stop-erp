using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Communications;
using One4All.Core.Payroll;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers HR expiry alerts: what lapses and who is told.</summary>
[Collection(AccountingCollection.Name)]
public sealed class HrExpiryAlertTests
{
    private readonly AccountingFixture _fixture;

    public HrExpiryAlertTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Expiring_permits_contracts_and_documents_are_flagged_to_hr()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        DateOnly today = new(2026, 3, 1);
        Employee ann = new(companyId, "HXA-1", "Ann Permit", 100m, new DateOnly(2024, 1, 1)) { WorkPermitExpiry = today.AddDays(10) };
        Employee ben = new(companyId, "HXA-2", "Ben Contract", 100m, new DateOnly(2025, 1, 1)) { EmploymentType = EmploymentType.FixedTerm, ContractEndDate = today.AddDays(45) };
        Employee cy = new(companyId, "HXA-3", "Cy Settled", 100m, new DateOnly(2020, 1, 1)) { WorkPermitExpiry = today.AddYears(2) };
        db.Employees.AddRange(ann, ben, cy);
        await db.SaveChangesAsync();
        db.Add(new EmployeeDocument(companyId, cy.Id, "Driving licence", "DL-9", today.AddDays(-2)));
        string tag = Guid.NewGuid().ToString("N")[..8];
        Role hr = new($"HR officer {tag}");
        hr.Grant("Payroll.Employee", OperationFlags.View | OperationFlags.Edit);
        db.Roles.Add(hr);
        User officer = new($"hxa-hr-{tag}", "HR Officer");
        db.Users.Add(officer);
        await db.SaveChangesAsync();
        officer.AssignRole(hr.Id);
        await db.SaveChangesAsync();

        IHrAlertService alerts = scope.ServiceProvider.GetRequiredService<IHrAlertService>();
        IReadOnlyList<HrExpiry> due = await alerts.ExpiringAsync(companyId, today, 30);
        due.Select(d => (d.EmployeeNumber, d.DaysLeft)).Should().Equal(("HXA-3", -2), ("HXA-1", 10));
        (await alerts.ExpiringAsync(companyId, today, 60)).Should().Contain(d => d.EmployeeNumber == "HXA-2" && d.What.Contains("contract ends"));

        (await alerts.NotifyAsync(companyId, today, 30)).Value.Should().BeGreaterThan(0);
        UserNotification note = await db.Set<UserNotification>().AsNoTracking().Where(n => n.CompanyId == companyId && n.UserId == officer.Id).SingleAsync();
        note.Body.Should().Contain("HXA-1 Ann Permit: Work permit lapses").And.Contain("DRIVING LICENCE DL-9 lapsed");
    }
}
