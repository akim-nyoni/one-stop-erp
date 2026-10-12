using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Payroll;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers grievances followed to an outcome, property issued and returned, and medical records restricted to the medical permission.</summary>
[Collection(AccountingCollection.Name)]
public sealed class HrCasesTests
{
    private readonly AccountingFixture _fixture;

    public HrCasesTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Grievances_property_and_medical_records_are_kept()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        long employeeId = (await scope.ServiceProvider.GetRequiredService<IPayrollSetupService>().CreateEmployeeAsync(companyId, new NewEmployee("HRC-1", "Case Person", 400m, new DateOnly(2025, 1, 1))))
            .Value;
        Role medical = new($"Medical officer {companyId}");
        medical.Grant(IHrCasesService.MedicalPermission, OperationFlags.View | OperationFlags.Edit);
        db.Roles.Add(medical);
        User nurse = new($"hrc-nurse-{companyId}", "Nurse");
        User clerk = new($"hrc-clerk-{companyId}", "Clerk");
        db.Users.AddRange(nurse, clerk);
        await db.SaveChangesAsync();
        nurse.AssignRole(medical.Id);
        await db.SaveChangesAsync();

        IHrCasesService cases = scope.ServiceProvider.GetRequiredService<IHrCasesService>();
        DateOnly day = new(2026, 3, 2);
        long grievance = (await cases.LodgeGrievanceAsync(companyId, "hrc-1", day, "Overtime not paid", "February overtime missing", "Payroll")).Value;
        (await cases.MoveGrievanceAsync(companyId, grievance, GrievanceStatus.Resolved, day, null)).Error.Code.Should().Be("Hr.GrievanceOutcome");
        (await cases.MoveGrievanceAsync(companyId, grievance, GrievanceStatus.Investigating, day, null)).IsSuccess.Should().BeTrue();
        (await cases.MoveGrievanceAsync(companyId, grievance, GrievanceStatus.Resolved, day.AddDays(5), "Paid in March run")).IsSuccess.Should().BeTrue();
        (await cases.MoveGrievanceAsync(companyId, grievance, GrievanceStatus.Investigating, day.AddDays(6), null)).Error.Code.Should().Be("Hr.GrievanceStage");

        long laptop = (await cases.IssueAssetAsync(companyId, "HRC-1", "Laptop", "SN-778", 900m, day)).Value;
        (await cases.IssueAssetAsync(companyId, "HRC-1", "Office keys", null, 0m, day)).IsSuccess.Should().BeTrue();
        (await cases.ReturnAssetAsync(companyId, laptop, day.AddDays(-1), null)).Error.Code.Should().Be("Hr.AssetReturned");
        (await cases.ReturnAssetAsync(companyId, laptop, day.AddDays(30), "Good")).IsSuccess.Should().BeTrue();
        (await cases.OutstandingAssetsAsync(companyId, employeeId)).Select(a => a.Description).Should().Equal("Office keys");

        (await cases.RecordMedicalAsync(companyId, "HRC-1", day, MedicalRecordKind.Fitness, "Fit for driving duties", true, day.AddDays(365), clerk.Id)).Error.Code.Should().Be("Hr.MedicalRestricted");
        (await cases.RecordMedicalAsync(companyId, "HRC-1", day, MedicalRecordKind.Fitness, "Fit for driving duties", true, day.AddDays(365), nurse.Id)).IsSuccess.Should().BeTrue();
        (await cases.MedicalRecordsAsync(companyId, "HRC-1", clerk.Id)).Error.Code.Should().Be("Hr.MedicalRestricted");
        (await cases.MedicalRecordsAsync(companyId, "HRC-1", nurse.Id)).Value.Should().ContainSingle().Which.Note.Should().Be("Fit for driving duties");
    }
}
