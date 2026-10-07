using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Schools;
using One4All.Application.Abstractions.Security;
using One4All.Core.Payroll;
using One4All.Core.Schools;
using One4All.Core.Security;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Security;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the school fee controls: a bursary reduces fees only once someone else with the approval right approves it, a staff member's child takes
/// the staff discount only while the parent is employed, and a do-not-disclose student's name is withheld from readers without the restricted right.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SchoolFeeControlTests
{
    private readonly AccountingFixture _fixture;

    public SchoolFeeControlTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Bursaries_are_approved_staff_children_discounted_and_restricted_students_withheld()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        long companyId, clerkId, bursarId, studentId, staffChildId, structureId;
        Employee parent;
        await using (AsyncServiceScope setup = provider.CreateAsyncScope())
        {
            One4AllDbContext db = setup.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Role bursar = new($"Bursar {companyId}"), registrar = new($"Registrar {companyId}");
            bursar.Grant("School.Bursary", OperationFlags.View | OperationFlags.Create | OperationFlags.Approve);
            registrar.Grant("School.Restricted", OperationFlags.View);
            db.Roles.AddRange(bursar, registrar);
            User clerk = new($"sfc-clerk-{companyId}", "Fees Clerk"), head = new($"sfc-bursar-{companyId}", "Bursar");
            db.Users.AddRange(clerk, head);
            Customer guardian = new(companyId, "SFC-G", "Guardian", Scenario.Usd);
            db.Customers.Add(guardian);
            parent = new Employee(companyId, "SFC-E1", "Teacher Parent", 3_000m, new DateOnly(2024, 1, 1));
            db.Employees.Add(parent);
            FeeStructure structure = new(companyId, "SFC-G1T1", "Grade 1 Term 1", "SFCG1", "Term 1", "4000");
            structure.AddItem("Tuition", 1_000m, true);
            db.FeeStructures.Add(structure);
            await db.SaveChangesAsync();
            clerk.AssignRole(bursar.Id);
            head.AssignRole(bursar.Id);
            head.AssignRole(registrar.Id);
            Student student = new(companyId, "SFC-1", "Bursary Pupil", "SFCG1", guardian.Id), staffChild = new(companyId, "SFC-2", "Staff Child", "SFCG1", guardian.Id);
            db.Students.AddRange(student, staffChild);
            await db.SaveChangesAsync();
            (companyId, clerkId, bursarId, studentId, staffChildId, structureId) = (companyId, clerk.Id, head.Id, student.Id, staffChild.Id, structure.Id);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISchoolBursaryService service = scope.ServiceProvider.GetRequiredService<ISchoolBursaryService>();
            ISchoolBillingService billing = scope.ServiceProvider.GetRequiredService<ISchoolBillingService>();
            DateOnly day = new(2026, 1, 10);

            // A requested bursary does nothing until someone other than the requester approves it.
            string reference = (await service.RequestAsync(companyId, studentId, BursaryType.Percentage, 25m, "Hardship", clerkId, day)).Value.Reference;
            (await service.RequestAsync(companyId, studentId, BursaryType.Value, 10m, "Again", clerkId, day)).Error.Code.Should().Be("School.BursaryPending");
            (await billing.BillStudentAsync(companyId, studentId, structureId, "SFC-INV-1", day)).Value.NetFees.Should().Be(1_000m);
            (await service.DecideAsync(companyId, reference, true, clerkId, day, null)).Error.Code.Should().Be("School.BursarySelfApproval");
            (await service.DecideAsync(companyId, reference, true, bursarId, day, "Means test passed")).IsSuccess.Should().BeTrue();
            (await billing.BillStudentAsync(companyId, studentId, structureId, "SFC-INV-2", day)).Value.NetFees.Should().Be(750m);

            // Staff child: 50% while the parent is employed (better than the 10% sibling discount); none once they leave.
            (await billing.SetSiblingDiscountAsync(companyId, 2, 10m)).IsSuccess.Should().BeTrue();
            (await service.SetStaffChildDiscountAsync(companyId, 50m)).IsSuccess.Should().BeTrue();
            (await service.SetStaffParentAsync(companyId, staffChildId, parent.Id)).IsSuccess.Should().BeTrue();
            StudentBillResult staffBill = (await billing.BillStudentAsync(companyId, staffChildId, structureId, "SFC-INV-3", day)).Value;
            (staffBill.NetFees, staffBill.StaffDiscount, staffBill.SiblingDiscount).Should().Be((500m, 500m, 0m));
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Employees.SingleAsync(e => e.Id == parent.Id)).Terminate();
            await db.SaveChangesAsync();
            StudentBillResult afterLeaving = (await billing.BillStudentAsync(companyId, staffChildId, structureId, "SFC-INV-4", day)).Value;
            (afterLeaving.NetFees, afterLeaving.StaffDiscount, afterLeaving.SiblingDiscount).Should().Be((900m, 0m, 100m));

            (await service.SetDisclosureAsync(companyId, staffChildId, true, null)).Error.Code.Should().Be("School.DisclosureNote");
            (await service.SetDisclosureAsync(companyId, staffChildId, true, "Court order")).IsSuccess.Should().BeTrue();
        }

        // The register withholds the restricted student's name from the clerk but shows it to the registrar.
        async Task<IReadOnlyList<string?>> Names(long userId)
        {
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            EffectivePermissions permissions = await scope.ServiceProvider.GetRequiredService<IPermissionService>().ResolveAsync(userId);
            scope.ServiceProvider.GetRequiredService<CurrentUserContext>().SignIn(userId, "reader", permissions, companyId, "test");
            IReportDataProvider register = scope.ServiceProvider.GetServices<IReportDataProvider>().First(p => p.CanHandle("StudentRegister"));
            return [.. (await register.GetRowsAsync(companyId, "StudentRegister", new Dictionary<string, object?>())).Select(r => r["FullName"] as string)];
        }

        (await Names(clerkId)).Should().Equal("Bursary Pupil", "Restricted record");
        (await Names(bursarId)).Should().Equal("Bursary Pupil", "Staff Child");
    }
}
