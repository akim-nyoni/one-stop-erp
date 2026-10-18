using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers waiting-list integrity: a registration-date change waits for a second officer (the requester cannot approve,
/// a second request is refused while one is pending) and only then moves seniority; a rejected change moves nothing.
/// On an annual cycle with 30 days' grace, the renewal run suspends an applicant who has not confirmed, removes one long
/// overdue, and leaves one who confirmed; a suspended applicant who confirms is reinstated; the notice history records
/// each step.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class WaitingListControlTests
{
    private readonly AccountingFixture _fixture;

    public WaitingListControlTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Date_changes_need_two_officers_and_renewals_suspend_then_remove()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);

        IWaitingListService list = scope.ServiceProvider.GetRequiredService<IWaitingListService>();
        async Task<string> Register(string id, DateOnly registered) => (await list.RegisterAsync(new WaitingListApplication
        {
            CompanyId = companyId, IdNumber = $"{id}-{companyId}", FullName = id, Category = HousingCategory.HighDensity, DateOfBirth = new DateOnly(1980, 1, 1),
            ResidentSince = new DateOnly(2000, 1, 1), MonthlyIncome = 500m, Tenure = CurrentTenure.Tenant, RegistrationDate = registered,
        })).Value.WaitingListNumber;
        string quiet = await Register("WC-QUIET", new DateOnly(2025, 3, 1));
        string lapsed = await Register("WC-LAPSED", new DateOnly(2024, 1, 10));
        string keen = await Register("WC-KEEN", new DateOnly(2025, 3, 1));

        IWaitingListControlService controls = scope.ServiceProvider.GetRequiredService<IWaitingListControlService>();
        long request = (await controls.RequestDateChangeAsync(companyId, quiet, new DateOnly(2023, 6, 1), "Paper file shows earlier lodgement", 71)).Value;
        (await controls.RequestDateChangeAsync(companyId, quiet, new DateOnly(2024, 7, 1), "Again", 71)).Error.Code.Should().Be("Hse.DateChangePending");
        (await list.EnquireAsync(companyId, quiet, new DateOnly(2026, 4, 1))).Value.Score.Lines[0].Detail.Should().StartWith("1 ×"); // unchanged until approved
        (await controls.DecideDateChangeAsync(companyId, request, approve: true, 71)).Error.Code.Should().Be("Hse.SelfApproval");
        (await controls.DecideDateChangeAsync(companyId, request, approve: true, 72)).IsSuccess.Should().BeTrue();
        (await list.EnquireAsync(companyId, quiet, new DateOnly(2026, 4, 1))).Value.Score.Lines[0].Detail.Should().StartWith("2 ×"); // moved to June 2023
        long rejected = (await controls.RequestDateChangeAsync(companyId, keen, new DateOnly(2020, 1, 1), "Claims earlier date", 71)).Value;
        (await controls.DecideDateChangeAsync(companyId, rejected, approve: false, 72)).IsSuccess.Should().BeTrue();
        (await controls.DecideDateChangeAsync(companyId, rejected, approve: true, 73)).Error.Code.Should().Be("Hse.DateChangeDecided");

        (await controls.ConfirmAsync(companyId, keen, new DateOnly(2026, 2, 1))).IsSuccess.Should().BeTrue();
        // Annual cycle, 30 days' grace, run on 10 March 2026: quiet (registration moved to June 2023, so due June 2024) and lapsed (due 10 January 2025)
        // are overdue - suspended and told first, never removed in one step; keen confirmed in February → untouched. No phone numbers: letters.
        (await controls.RunRenewalsAsync(companyId, new DateOnly(2026, 3, 10), 12, 30)).Should().Be(new RenewalRunResult(2, 0, 0, 2));

        string fresh = await Register("WC-FRESH", new DateOnly(2025, 2, 20));
        (await controls.RunRenewalsAsync(companyId, new DateOnly(2026, 3, 1), 12, 30)).Should().Be(new RenewalRunResult(1, 0, 0, 1));
        (await list.EnquireAsync(companyId, fresh, new DateOnly(2026, 3, 1))).Value.Status.Should().Be(ApplicantStatus.Suspended);
        (await controls.ConfirmAsync(companyId, fresh, new DateOnly(2026, 3, 5))).IsSuccess.Should().BeTrue();
        (await list.EnquireAsync(companyId, fresh, new DateOnly(2026, 3, 5))).Value.Status.Should().Be(ApplicantStatus.Active);
        // Thirty days after their suspension notices, quiet and lapsed are removed - and told.
        (await controls.RunRenewalsAsync(companyId, new DateOnly(2026, 4, 9), 12, 30)).Should().Be(new RenewalRunResult(0, 2, 0, 2));
        (await controls.ConfirmAsync(companyId, lapsed, new DateOnly(2026, 4, 10))).Error.Code.Should().Be("Hse.NotOnList");

        const string letter = "Letter";
        (await controls.NoticesAsync(companyId, fresh)).Should().Equal(new ApplicantNoticeEntry(ApplicantNoticeType.Suspended, new DateOnly(2026, 3, 1), letter, "(no address on file)"));
        (await controls.NoticesAsync(companyId, lapsed)).Should().Equal(new ApplicantNoticeEntry(ApplicantNoticeType.Suspended, new DateOnly(2026, 3, 10), letter, "(no address on file)"),
            new ApplicantNoticeEntry(ApplicantNoticeType.Removed, new DateOnly(2026, 4, 9), letter, "(no address on file)"));
    }
}
