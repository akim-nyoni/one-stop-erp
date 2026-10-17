using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers waiting-list intake: contact details; documents verified one by one (the applicant once all are); the duplicate queue - the same phone under two IDs
/// and an applicant on the deceased register are queued once, merged keeping the earlier registration, rejected and cleared with reasons.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ApplicantIntakeTests
{
    private readonly AccountingFixture _fixture;

    public ApplicantIntakeTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Intake_verifies_documents_and_queues_duplicates()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IWaitingListService list = scope.ServiceProvider.GetRequiredService<IWaitingListService>();
        string suffix = (companyId % 1000000).ToString("000000");
        async Task<string> Register(string id, string name, int year) => (await list.RegisterAsync(new WaitingListApplication
        {
            CompanyId = companyId, IdNumber = id, FullName = name, Category = HousingCategory.HighDensity, DateOfBirth = new DateOnly(1980, 1, 1), ResidentSince = new DateOnly(2000, 1, 1),
            MonthlyIncome = 500m, Tenure = CurrentTenure.Lodger, RegistrationDate = new DateOnly(year, 1, 1),
        })).Value.WaitingListNumber;
        string tendai = await Register($"11-{suffix}A11", "Tendai Moyo", 2020);
        string other = await Register($"22-{suffix}B22", "Rudo Ncube", 2023);
        string late = await Register($"33-{suffix}C33", "Farai Dube", 2024);

        IApplicantIntakeService intake = scope.ServiceProvider.GetRequiredService<IApplicantIntakeService>();
        (await intake.SetContactAsync(companyId, tendai, new ApplicantContact(Core.Payroll.Gender.Female, "4 Jacaranda St", "PO Box 9", "+263 77 123 4567", "City Council"))).IsSuccess.Should().BeTrue();
        (await intake.SetContactAsync(companyId, other, new ApplicantContact(Core.Payroll.Gender.Male, null, null, "077 123 4567", null))).IsSuccess.Should().BeTrue();

        long idCopy = (await intake.LodgeDocumentAsync(companyId, tendai, ApplicantDocumentType.IdCopy, "ID copy", new DateOnly(2026, 3, 1))).Value;
        long residence = (await intake.LodgeDocumentAsync(companyId, tendai, ApplicantDocumentType.ProofOfResidence, "Utility bill", new DateOnly(2026, 3, 1))).Value;
        (await intake.VerifyDocumentAsync(companyId, idCopy, "", new DateOnly(2026, 3, 2))).Error.Code.Should().Be("Hse.Officer");
        (await intake.VerifyDocumentAsync(companyId, idCopy, "Officer Banda", new DateOnly(2026, 3, 2))).Value.Should().BeFalse("the proof of residence still waits");
        (await intake.VerifyDocumentAsync(companyId, residence, "Officer Banda", new DateOnly(2026, 3, 2))).Value.Should().BeTrue();
        (await db.HousingApplicants.AsNoTracking().SingleAsync(a => a.CompanyId == companyId && a.WaitingListNumber == tendai)).IsVerified.Should().BeTrue();

        (await intake.RecordDeathAsync(companyId, $"33-{suffix}C33", "Farai Dube", new DateOnly(2025, 12, 1))).IsSuccess.Should().BeTrue();
        DuplicateScan scan = await intake.ScanAsync(companyId, new DateOnly(2026, 3, 5));
        (scan.Opened, scan.Deceased).Should().Be((2, 1));
        (await intake.ScanAsync(companyId, new DateOnly(2026, 3, 6))).Opened.Should().Be(0, "already queued");

        IReadOnlyList<DuplicateCaseView> queue = await intake.QueueAsync(companyId);
        DuplicateCaseView phone = queue.Single(q => q.Reason.Contains("phone"));
        DuplicateCaseView dead = queue.Single(q => q.Reason.Contains("deceased"));
        (await intake.DecideAsync(companyId, phone.Id, DuplicateCaseStatus.Merged, "", 1, new DateOnly(2026, 3, 7))).Error.Code.Should().Be("Hse.Decision");
        (await intake.DecideAsync(companyId, phone.Id, DuplicateCaseStatus.Merged, "Same household; one registration kept", 1, new DateOnly(2026, 3, 7))).IsSuccess.Should().BeTrue();
        (await db.HousingApplicants.AsNoTracking().SingleAsync(a => a.CompanyId == companyId && a.WaitingListNumber == other)).Status.Should().Be(ApplicantStatus.Removed, "the later registration goes");
        (await db.HousingApplicants.AsNoTracking().SingleAsync(a => a.CompanyId == companyId && a.WaitingListNumber == tendai)).Status.Should().Be(ApplicantStatus.Active);
        (await intake.DecideAsync(companyId, dead.Id, DuplicateCaseStatus.Rejected, "Applicant deceased", 1, new DateOnly(2026, 3, 7))).IsSuccess.Should().BeTrue();
        (await db.HousingApplicants.AsNoTracking().SingleAsync(a => a.CompanyId == companyId && a.WaitingListNumber == late)).Status.Should().Be(ApplicantStatus.Removed);
        (await intake.DecideAsync(companyId, dead.Id, DuplicateCaseStatus.Cleared, "again", 1, new DateOnly(2026, 3, 7))).Error.Code.Should().Be("Hse.CaseDecided");
    }
}
