using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Schools;
using One4All.Core.Schools;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers setting up a school from scratch and billing its term: structures, items, students, hostels and routes.</summary>
[Collection(AccountingCollection.Name)]
public sealed class SchoolSetupTests
{
    private readonly AccountingFixture _fixture;

    public SchoolSetupTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_school_is_set_up_and_billed_for_the_term()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Customers.AddRange(new Customer(companyId, "SS-PAR1", "Parent One", Scenario.Usd), new Customer(companyId, "SS-PAR2", "Parent Two", Scenario.Usd));
        await db.SaveChangesAsync();

        ISchoolSetupService setup = scope.ServiceProvider.GetRequiredService<ISchoolSetupService>();
        (await setup.SaveFeeStructureAsync(companyId, "G1-T1", "Grade 1 term 1", "G1", "2026-T1", "9999")).Error.Code.Should().Be("School.AccountNotFound");
        long structure = (await setup.SaveFeeStructureAsync(companyId, "g1-t1", "Grade 1 term 1", "G1", "2026-T1", "4200")).Value;
        (await setup.AddFeeItemAsync(companyId, "G1-T1", "Tuition", 500m, true, null)).IsSuccess.Should().BeTrue();
        (await setup.AddFeeItemAsync(companyId, "G1-T1", "Levy", 0m, true, null)).Error.Code.Should().Be("School.FeeItem");
        (await setup.AddFeeItemAsync(companyId, "G1-T1", "Swimming", 40m, false, null)).IsSuccess.Should().BeTrue();

        (await setup.AdmitStudentAsync(companyId, "ss-1", "Tari One", "G1", "SS-PAR1", new DateOnly(2026, 1, 10))).IsSuccess.Should().BeTrue();
        (await setup.AdmitStudentAsync(companyId, "SS-1", "Again", "G1", "SS-PAR1", new DateOnly(2026, 1, 10))).Error.Code.Should().Be("School.StudentExists");
        (await setup.AdmitStudentAsync(companyId, "SS-2", "Rudo Two", "G1", "SS-PAR2", new DateOnly(2026, 1, 10))).IsSuccess.Should().BeTrue();

        (await setup.SaveHostelAsync(companyId, "SS-H1", "Girls' hostel", 1, 300m, "4200")).IsSuccess.Should().BeTrue();
        (await setup.PlaceStudentAsync(companyId, "SS-1", "SS-H1", null)).IsSuccess.Should().BeTrue();
        (await setup.PlaceStudentAsync(companyId, "SS-2", "SS-H1", null)).Error.Code.Should().Be("School.HostelFull");
        (await setup.SetSponsorAsync(companyId, "SS-2", "NO-SUCH")).Error.Code.Should().Be("School.SponsorNotFound");
        (await setup.SetSponsorAsync(companyId, "SS-2", "ss-par2")).IsSuccess.Should().BeTrue();
        (await db.Students.AsNoTracking().SingleAsync(s => s.CompanyId == companyId && s.AdmissionNumber == "SS-2")).SponsorCustomerId.Should().NotBeNull();
        (await setup.SetSponsorAsync(companyId, "SS-2", null)).IsSuccess.Should().BeTrue();
        (await db.Students.AsNoTracking().SingleAsync(s => s.CompanyId == companyId && s.AdmissionNumber == "SS-2")).SponsorCustomerId.Should().BeNull();
        (await db.Students.AsNoTracking().SingleAsync(s => s.CompanyId == companyId && s.AdmissionNumber == "SS-1")).IsBoarder.Should().BeTrue();

        TermBillResult term = (await scope.ServiceProvider.GetRequiredService<ISchoolBillingService>().BillTermAsync(companyId, structure, "SS-T1-", new DateOnly(2026, 1, 15))).Value;
        term.Bills.Should().HaveCount(2);
        term.Bills.Should().OnlyContain(b => b.GrossFees >= 500m);
    }
}
