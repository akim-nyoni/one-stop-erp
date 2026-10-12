using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers the rest of the employee master: contacts and post details, the photograph, beneficiaries' shares and medical-aid dependants, and the further contract types.</summary>
[Collection(AccountingCollection.Name)]
public sealed class EmployeeProfileTests
{
    private readonly AccountingFixture _fixture;

    public EmployeeProfileTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Employee_master_holds_contacts_photo_and_dependants()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IPayrollSetupService setup = scope.ServiceProvider.GetRequiredService<IPayrollSetupService>();
        IEmployeeProfileService profile = scope.ServiceProvider.GetRequiredService<IEmployeeProfileService>();

        // The further contract types need an end date, as a contract does.
        (await setup.CreateEmployeeAsync(companyId, new NewEmployee("EP-1", "Intern", 200m, new DateOnly(2026, 1, 1)) { EmploymentType = EmploymentType.Intern }))
            .Error.Code.Should().Be("Payroll.ContractEnd");
        (await setup.CreateEmployeeAsync(companyId, new NewEmployee("EP-1", "Tendai Ncube", 800m, new DateOnly(2026, 1, 1))
        {
            EmploymentType = EmploymentType.FixedTerm, ContractEndDate = new DateOnly(2026, 12, 31),
        })).IsSuccess.Should().BeTrue();

        (await profile.SetDetailsAsync(companyId, "ep-1", new EmployeeProfileUpdate
        {
            PhysicalAddress = "12 Main St, Harare", PostalAddress = "PO Box 9", Phone = "+263 77 123 4567", JobTitle = "Accounts clerk", NoticePeriodDays = 30, WorkersCompensationClass = "c1",
        })).IsSuccess.Should().BeTrue();
        (await profile.SetDetailsAsync(companyId, "EP-1", new EmployeeProfileUpdate { PostalAddress = " ", NoticePeriodDays = 400 })).Error.Code.Should().Be("Hr.NoticePeriod");
        (await profile.SetDetailsAsync(companyId, "EP-1", new EmployeeProfileUpdate { PostalAddress = " " })).IsSuccess.Should().BeTrue();

        (await profile.SetPhotoAsync(companyId, "EP-1", [1, 2, 3], "image/gif")).Error.Code.Should().Be("Hr.Photo");
        (await profile.SetPhotoAsync(companyId, "EP-1", [0xFF, 0xD8, 0xFF], "image/jpeg")).IsSuccess.Should().BeTrue();

        Employee tendai = await db.Employees.AsNoTracking().SingleAsync(e => e.CompanyId == companyId && e.EmployeeNumber == "EP-1");
        (tendai.PhysicalAddress, tendai.PostalAddress, tendai.JobTitle, tendai.NoticePeriodDays, tendai.WorkersCompensationClass, tendai.PhotoContentType)
            .Should().Be(("12 Main St, Harare", (string?)null, "Accounts clerk", (int?)30, "C1", "image/jpeg"));
        tendai.Photo.Should().Equal(0xFF, 0xD8, 0xFF);

        // Beneficiaries share at most 100%; medical-aid dependants carry their member number.
        (await profile.AddDependantAsync(companyId, "EP-1", new NewDependant(DependantKind.Both, "Rudo Ncube", "Spouse") { SharePercent = 60m, MedicalAidNumber = "MA-1/01" }))
            .IsSuccess.Should().BeTrue();
        (await profile.AddDependantAsync(companyId, "EP-1", new NewDependant(DependantKind.Beneficiary, "Tafadzwa Ncube", "Child") { SharePercent = 50m }))
            .Error.Code.Should().Be("Hr.BeneficiaryShares");
        long child = (await profile.AddDependantAsync(companyId, "EP-1", new NewDependant(DependantKind.Beneficiary, "Tafadzwa Ncube", "Child") { SharePercent = 40m })).Value;
        (await profile.AddDependantAsync(companyId, "EP-1", new NewDependant(DependantKind.Beneficiary, "No share", "Sibling"))).Error.Code.Should().Be("Hr.BeneficiaryShare");
        (await profile.AddDependantAsync(companyId, "EP-1", new NewDependant(DependantKind.MedicalAidDependant, "Baby Ncube", "Child") { MedicalAidNumber = "MA-1/02" }))
            .IsSuccess.Should().BeTrue();

        IReadOnlyList<EmployeeDependant> listed = await profile.DependantsAsync(companyId, "EP-1");
        listed.Should().HaveCount(3);
        listed.Single(d => d.Name == "Rudo Ncube").MedicalAidNumber.Should().Be("MA-1/01");
        (await profile.RemoveDependantAsync(companyId, child)).IsSuccess.Should().BeTrue();
        (await profile.DependantsAsync(companyId, "EP-1")).Should().HaveCount(2);
    }
}
