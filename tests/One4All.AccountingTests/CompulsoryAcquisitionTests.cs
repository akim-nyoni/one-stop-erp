using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the compulsory acquisition sub-process: the notice opens an objection window, the assessment, an accepted offer or an award moves the
/// provision, part payments come out of it, possession waits for the window to close with no objection standing, and the register shows it all.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class CompulsoryAcquisitionTests
{
    private readonly AccountingFixture _fixture;

    public CompulsoryAcquisitionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Notice_objections_assessment_offers_award_payments_and_possession()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "2280", "Compensation Provisions", AccountType.Liability));
        await db.SaveChangesAsync();
        long parcelId = (await scope.ServiceProvider.GetRequiredService<IHousingService>().AcquireLandAsync(companyId, $"LP-CA-{companyId}", "Expropriated plot", 5000m, 100000m,
            new DateOnly(2026, 1, 10), "1450", "1100", "LAND-CA")).Value.LandParcelId;

        ICompensationService compensation = scope.ServiceProvider.GetRequiredService<ICompensationService>();
        string claim = (await compensation.LodgeAsync(companyId, parcelId, "Owner Family", new DateOnly(2026, 2, 1), "1450", "2280")).Value;
        (await compensation.TakePossessionAsync(companyId, claim, new DateOnly(2026, 2, 2))).Error.Code.Should().Be("Hse.PossessionEarly");
        (await compensation.LodgeObjectionAsync(companyId, claim, "Neighbour", "Access road", new DateOnly(2026, 2, 3))).Error.Code.Should().Be("Hse.ObjectionWindow");

        // Notice on 1 March with 30 days to object.
        (await compensation.ServeNoticeAsync(companyId, claim, "CAN-2026-01", new DateOnly(2026, 3, 1), 30)).IsSuccess.Should().BeTrue();
        long objection = (await compensation.LodgeObjectionAsync(companyId, claim, "Neighbour", "Access road", new DateOnly(2026, 3, 10))).Value;
        (await compensation.LodgeObjectionAsync(companyId, claim, "Late Objector", "Too late", new DateOnly(2026, 4, 5))).Error.Code.Should().Be("Hse.ObjectionWindow");

        // Assessed at 40,000 (provided); a 38,000 offer is rejected; arbitration awards 45,000 (provided).
        (await compensation.OfferAsync(companyId, claim, 38_000m, new DateOnly(2026, 3, 20))).Error.Code.Should().Be("Hse.OfferBeforeAssessment");
        (await compensation.AssessAsync(companyId, claim, "District Valuer", new DateOnly(2026, 4, 1), 40_000m)).IsSuccess.Should().BeTrue();
        long offer = (await compensation.OfferAsync(companyId, claim, 38_000m, new DateOnly(2026, 4, 2))).Value;
        (await compensation.OfferAsync(companyId, claim, 39_000m, new DateOnly(2026, 4, 3))).Error.Code.Should().Be("Hse.OfferOpen");
        (await compensation.RespondToOfferAsync(companyId, claim, offer, false, new DateOnly(2026, 4, 9))).IsSuccess.Should().BeTrue();
        long dispute = (await compensation.DisputeAsync(companyId, claim, "Land Arbitrator", new DateOnly(2026, 4, 10), "Owner seeks 50,000")).Value;

        // Possession waits for the objection to be decided.
        (await compensation.TakePossessionAsync(companyId, claim, new DateOnly(2026, 4, 15))).Error.Code.Should().Be("Hse.PossessionObjected");
        (await compensation.DecideObjectionAsync(companyId, claim, objection, false, new DateOnly(2026, 4, 20), "Access preserved")).IsSuccess.Should().BeTrue();
        (await compensation.TakePossessionAsync(companyId, claim, new DateOnly(2026, 4, 30))).IsSuccess.Should().BeTrue();

        (await compensation.AwardAsync(companyId, claim, dispute, 45_000m, new DateOnly(2026, 5, 15))).IsSuccess.Should().BeTrue();
        (await compensation.PayPartAsync(companyId, claim, 20_000m, "1100", new DateOnly(2026, 6, 1))).IsSuccess.Should().BeTrue();
        (await compensation.PayPartAsync(companyId, claim, 30_000m, "1100", new DateOnly(2026, 6, 2))).Error.Code.Should().Be("Hse.PartPayment");
        (await compensation.SettleAsync(companyId, claim, 25_000m, "1100", new DateOnly(2026, 7, 1))).IsSuccess.Should().BeTrue();

        // The land carries its 100,000 price plus the 45,000 awarded; the provision is spent; 145,000 left the bank.
        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 12, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        decimal Net(string code) => tb.Rows.Where(r => r.AccountCode == code).Sum(r => r.Debit - r.Credit);
        (Net("1450"), Net("2280"), Net("1100")).Should().Be((145_000m, 0m, -145_000m));
        (await compensation.RegisterAsync(companyId)).Single().SettledAmount.Should().Be(45_000m);

        IReportDataProvider register = scope.ServiceProvider.GetServices<IReportDataProvider>().First(p => p.CanHandle("HousingCompensation"));
        IReadOnlyDictionary<string, object?> row = (await register.GetRowsAsync(companyId, "HousingCompensation", new Dictionary<string, object?>())).Single();
        (row["Objections"], row["Assessed"], row["Offered"], row["Paid"], row["Outstanding"]).Should().Be(("0 open of 1", 40_000m, 0m, 45_000m, 0m));
        ((string)row["Possession"]!).Should().NotBeEmpty();
        ((string)row["Notice"]!).Should().NotBeEmpty();
    }
}
