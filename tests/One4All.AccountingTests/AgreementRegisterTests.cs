using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the agreement register: an instalment-sale agreement is drawn up with its generated document; it cannot be
/// amended before signature or signed with one witness; signed with two it is in force; an amendment creates version 2
/// with its own document while version 1 stays in force until version 2 is signed; the history keeps both; a version
/// cannot be signed twice; and a stored document altered behind the system's back is caught by verification.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AgreementRegisterTests
{
    private readonly AccountingFixture _fixture;

    public AgreementRegisterTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Agreements_are_versioned_signed_and_tamper_evident()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer buyer = new(companyId, "AG-BUY", "Agreement Buyer", Scenario.Usd);
        db.Customers.Add(buyer);
        LandParcel parcel = new(companyId, $"LP-AG-{companyId}", "Agreement parcel", 5000m, 50000m);
        db.LandParcels.Add(parcel);
        await db.SaveChangesAsync();
        HousingStand stand = new(companyId, parcel.Id, null, "AG-1", 300m, 5000m);
        db.HousingStands.Add(stand);
        await db.SaveChangesAsync();

        IAgreementRegisterService agreements = scope.ServiceProvider.GetRequiredService<IAgreementRegisterService>();
        string number = (await agreements.DraftAsync(new AgreementDraft
        {
            CompanyId = companyId, AgreementType = AgreementType.InstalmentSale, PartyCustomerId = buyer.Id, StandId = stand.Id, Price = 13000m,
            Terms = "1,000 down, 12 monthly instalments at 12%", SpecialConditions = "Build within 2 years", Date = new DateOnly(2026, 1, 5),
        })).Value;

        (await agreements.AmendAsync(companyId, number, 12500m, "Revised", null, "Discount")).Error.Code.Should().Be("Hse.LatestUnsigned");
        (await agreements.SignAsync(companyId, number, new DateOnly(2026, 1, 10), ["Witness A"])).Error.Code.Should().Be("Hse.Witnesses");
        (await agreements.SignAsync(companyId, number, new DateOnly(2026, 1, 10), ["Witness A", "Witness B"])).IsSuccess.Should().BeTrue();
        (await agreements.SignAsync(companyId, number, new DateOnly(2026, 1, 11), ["Witness A", "Witness B"])).Error.Code.Should().Be("Hse.AlreadySigned");

        (await agreements.AmendAsync(companyId, number, 12500m, "1,000 down, 12 monthly instalments at 10%", "Build within 3 years", "Council resolution 14/2026")).Value.Should().Be(2);
        IReadOnlyList<AgreementVersionEntry> pending = (await agreements.HistoryAsync(companyId, number)).Value;
        pending.Select(v => (v.Version, v.InForce)).Should().Equal((1, true), (2, false));
        pending[1].Document.Should().Contain("version 2").And.Contain("12,500.00").And.Contain("Build within 3 years");

        (await agreements.SignAsync(companyId, number, new DateOnly(2026, 2, 1), ["Witness C", "Witness D"])).IsSuccess.Should().BeTrue();
        IReadOnlyList<AgreementVersionEntry> history = (await agreements.HistoryAsync(companyId, number)).Value;
        history.Select(v => (v.Version, v.InForce, v.AmendmentReason)).Should().Equal((1, false, (string?)null), (2, true, "Council resolution 14/2026"));
        history[0].Document.Should().Contain("13,000.00"); // the superseded version is kept as it was

        (await agreements.VerifyDocumentsAsync(companyId)).Should().BeEmpty();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE hse.agreement_version SET document = replace(document, '13,000.00', '1,300.00') WHERE version = 1 AND housing_agreement_id = (SELECT id FROM hse.housing_agreement WHERE company_id = {companyId} AND agreement_number = {number})");
        (await agreements.VerifyDocumentsAsync(companyId)).Should().Equal($"{number} v1");
    }
}
