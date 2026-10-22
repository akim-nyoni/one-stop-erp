using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Housing;
using One4All.Application.Municipal;
using One4All.Core.Housing;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the housing waiting list: with a five-year residency rule, an older registrant and a newer but needier one
/// register (the newer one homeless with dependants); a duplicate ID, a newcomer and an applicant who already owns a
/// municipal property are refused; ranking by seniority puts the older first and by score the needier first; the
/// enquiry by ID gives the position and the score explanation.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class WaitingListTests
{
    private readonly AccountingFixture _fixture;

    public WaitingListTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Applicants_register_after_checks_and_rank_by_seniority_or_score()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);

        // An existing municipal owner, matched on national ID.
        Customer owner = new(companyId, "WL-OWN", "Existing Owner", Scenario.Usd) { NationalIdNumber = "63-OWN-1" };
        db.Customers.Add(owner);
        await db.SaveChangesAsync();
        Property home = new(companyId, "W1", "Ward 17", owner.Id);
        db.Properties.Add(home);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IOwnershipService>().RegisterOwnersAsync(companyId, home.Id, [new OwnerShare(owner.Id, 100m, true)], new DateOnly(2015, 1, 1), null)).IsSuccess.Should().BeTrue();

        IWaitingListService list = scope.ServiceProvider.GetRequiredService<IWaitingListService>();
        (await list.SetPolicyAsync(companyId, new WaitingListPolicyInput(new WaitingListTerms(18, 5, 3000m, 10m, 2m, 5m, 15m, 20m, 8m)))).IsSuccess.Should().BeTrue();

        WaitingListApplication Apply(string id, string name, DateOnly registered, int residentYear = 2005, int dependants = 0, CurrentTenure tenure = CurrentTenure.Tenant) => new()
        {
            CompanyId = companyId, IdNumber = id, FullName = name, Category = HousingCategory.HighDensity, DateOfBirth = new DateOnly(1985, 1, 1),
            ResidentSince = new DateOnly(residentYear, 1, 1), Dependants = dependants, MonthlyIncome = 1200m, Tenure = tenure, RegistrationDate = registered,
        };

        WaitingListRegistration older = (await TestClock.RegisterCapturedAsync(scope.ServiceProvider, Apply("63-111 A", "Older Registrant", new DateOnly(2018, 3, 1)))).Value;
        older.WaitingListNumber.Should().StartWith("WL-");
        WaitingListRegistration needier = (await TestClock.RegisterCapturedAsync(scope.ServiceProvider, Apply("63-222", "Needier Registrant", new DateOnly(2023, 3, 1), dependants: 4, tenure: CurrentTenure.Homeless))).Value;

        (await TestClock.RegisterCapturedAsync(scope.ServiceProvider, Apply("63-111 a", "Same Person", new DateOnly(2024, 1, 1)))).Error.Code.Should().Be("Hse.DuplicateApplicant");
        (await TestClock.RegisterCapturedAsync(scope.ServiceProvider, Apply("63-333", "Newcomer", new DateOnly(2024, 1, 1), residentYear: 2022))).Error.Message.Should().Contain("5 required");
        (await TestClock.RegisterCapturedAsync(scope.ServiceProvider, Apply("63-OWN-1", "Existing Owner", new DateOnly(2024, 1, 1)))).Error.Message.Should().Contain("Already owns");

        DateOnly asAt = new(2026, 6, 1);
        (await list.RankingAsync(companyId, HousingCategory.HighDensity, WaitingListOrder.Seniority, asAt)).Select(p => p.WaitingListNumber)
            .Should().Equal(older.WaitingListNumber, needier.WaitingListNumber);
        // Older: 8 years on the list (80) + 21 resident (42) = 122. Needier: 3 years (30) + 42 + 4 dependants (20) + homeless (20) = 112.
        IReadOnlyList<WaitingListPlace> byScore = await list.RankingAsync(companyId, HousingCategory.HighDensity, WaitingListOrder.Score, asAt);
        byScore.Select(p => p.Score).Should().Equal(122m, 112m);

        WaitingListEnquiry enquiry = (await list.EnquireAsync(companyId, " 63-222 ", asAt)).Value;
        enquiry.PositionBySeniority.Should().Be(2);
        enquiry.Score.Total.Should().Be(112m);
        enquiry.Score.Lines.Single(l => l.Factor == "Housing conditions").Detail.Should().Be("Homeless");
        (await list.EnquireAsync(companyId, "NOBODY", asAt)).Error.Code.Should().Be("Hse.ApplicantNotFound");
    }

    /// <summary>
    /// Audit #31: a registration is dated the day it is captured. An earlier date keyed at capture is refused (it would queue the applicant ahead of
    /// those registered in between); left unset it is today; a genuinely earlier date is registered today and then moved by the dual-authorised
    /// registration date change.
    /// </summary>
    [SkippableFact]
    public async Task A_registration_is_dated_the_day_it_is_captured()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        TestClock clock = scope.ServiceProvider.GetRequiredService<TestClock>();
        DateOnly today = new(2026, 5, 10);
        clock.Today = today;

        IWaitingListService list = scope.ServiceProvider.GetRequiredService<IWaitingListService>();
        WaitingListApplication Apply(string id, DateOnly registered = default) => new()
        {
            CompanyId = companyId, IdNumber = $"{id}-{companyId}", FullName = $"Applicant {id}", Category = HousingCategory.HighDensity, DateOfBirth = new DateOnly(1985, 1, 1),
            ResidentSince = new DateOnly(2000, 1, 1), MonthlyIncome = 500m, Tenure = CurrentTenure.Tenant, RegistrationDate = registered,
        };

        (await list.RegisterAsync(Apply("BD-1", new DateOnly(2010, 1, 1)))).Error.Code.Should().Be("Hse.BackdatedRegistration");
        (await list.RegisterAsync(Apply("BD-1", today.AddDays(-1)))).Error.Code.Should().Be("Hse.BackdatedRegistration");
        (await list.RegisterAsync(Apply("BD-1", today.AddMonths(1)))).Error.Code.Should().Be("Hse.RegistrationDate");
        string number = (await list.RegisterAsync(Apply("BD-1"))).Value.WaitingListNumber;
        (await list.RegisterAsync(Apply("BD-2", today))).IsSuccess.Should().BeTrue();
        (await db.HousingApplicants.AsNoTracking().SingleAsync(a => a.CompanyId == companyId && a.WaitingListNumber == number)).RegistrationDate.Should().Be(today);

        // The earlier date on the paper file goes through the registration date change, which a second user decides.
        IWaitingListControlService controls = scope.ServiceProvider.GetRequiredService<IWaitingListControlService>();
        long request = (await controls.RequestDateChangeAsync(companyId, number, new DateOnly(2010, 1, 1), "Paper file shows 2010 lodgement", 900_311)).Value;
        (await controls.DecideDateChangeAsync(companyId, request, approve: true, 900_311)).Error.Code.Should().Be("Hse.SelfApproval");
        (await controls.DecideDateChangeAsync(companyId, request, approve: true, 900_312)).IsSuccess.Should().BeTrue();
        (await db.HousingApplicants.AsNoTracking().SingleAsync(a => a.CompanyId == companyId && a.WaitingListNumber == number)).RegistrationDate.Should().Be(new DateOnly(2010, 1, 1));
    }
}
