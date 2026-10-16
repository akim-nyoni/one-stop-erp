using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Offers go back and forth on a purchase, the latest is agreed, and a purchase with open talks cannot complete.</summary>
[Collection(AccountingCollection.Name)]
public sealed class LandNegotiationTests
{
    private readonly AccountingFixture _fixture;

    public LandNegotiationTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Offers_are_exchanged_and_the_latest_is_agreed()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        ILandAcquisitionService land = scope.ServiceProvider.GetRequiredService<ILandAcquisitionService>();
        string number = (await land.OpenAsync(new AcquisitionCase
        {
            CompanyId = companyId, ParcelNumber = $"LP-NEG-{companyId}", Description = "Plot 7", AreaSquareMetres = 4_000m, Method = AcquisitionMethod.Purchase,
            Counterparty = "T. Moyo", ResolutionReference = "Res 12/2026", Date = new DateOnly(2026, 3, 1),
        })).Value;

        (await land.AgreePriceAsync(companyId, number, new DateOnly(2026, 3, 2))).Error.Code.Should().Be("Hse.NoOffer");
        (await land.OfferAsync(companyId, number, NegotiationParty.Counterparty, 60_000m, new DateOnly(2026, 3, 2), "Asking price")).Value.Should().Be(1);
        (await land.OfferAsync(companyId, number, NegotiationParty.Council, 45_000m, new DateOnly(2026, 3, 9), "Valuation-based")).Value.Should().Be(2);
        (await land.OfferAsync(companyId, number, NegotiationParty.Counterparty, 0m, new DateOnly(2026, 3, 10), null)).Error.Code.Should().Be("Hse.OfferAmount");

        // With talks open the purchase cannot complete, and the position says why.
        AcquisitionPosition open = (await land.PositionAsync(companyId, number)).Value;
        open.Outstanding.Should().Contain(o => o.StartsWith("Price not agreed: 2 offer(s)", StringComparison.Ordinal));
        (await land.CompleteAsync(companyId, number, new DateOnly(2026, 3, 20))).Error.Message.Should().Contain("Price not agreed");

        (await land.OfferAsync(companyId, number, NegotiationParty.Counterparty, 50_000m, new DateOnly(2026, 3, 15), "Split the difference")).Value.Should().Be(3);
        (await land.AgreePriceAsync(companyId, number, new DateOnly(2026, 3, 16))).Value.Should().Be(50_000m);
        (await land.OfferAsync(companyId, number, NegotiationParty.Council, 48_000m, new DateOnly(2026, 3, 17), null)).Error.Code.Should().Be("Hse.NegotiationClosed");

        LandAcquisition stored = await db.Set<LandAcquisition>().AsNoTracking().Include(a => a.Offers).SingleAsync(a => a.CompanyId == companyId && a.AcquisitionNumber == number);
        (stored.Negotiation, stored.AgreedPrice, stored.AgreedOn, stored.Offers.Count).Should().Be((NegotiationStatus.Agreed, 50_000m, new DateOnly(2026, 3, 16), 3));
        (await land.PositionAsync(companyId, number)).Value.Outstanding.Should().NotContain(o => o.StartsWith("Price not agreed", StringComparison.Ordinal));
    }

    [SkippableFact]
    public async Task Broken_off_talks_block_completion_until_resolved()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        long companyId = await Scenario.SeedAsync(scope.ServiceProvider.GetRequiredService<One4AllDbContext>());
        ILandAcquisitionService land = scope.ServiceProvider.GetRequiredService<ILandAcquisitionService>();
        string number = (await land.OpenAsync(new AcquisitionCase
        {
            CompanyId = companyId, ParcelNumber = $"LP-BRK-{companyId}", Description = "Plot 9", AreaSquareMetres = 1_000m, Method = AcquisitionMethod.Purchase,
            Counterparty = "Estate", ResolutionReference = "Res 13/2026", Date = new DateOnly(2026, 3, 1),
        })).Value;
        (await land.OfferAsync(companyId, number, NegotiationParty.Council, 10_000m, new DateOnly(2026, 3, 2), null)).IsSuccess.Should().BeTrue();
        (await land.BreakOffAsync(companyId, number, "")).Error.Code.Should().Be("Hse.BreakOffReason");
        (await land.BreakOffAsync(companyId, number, "Owner withdrew")).IsSuccess.Should().BeTrue();
        (await land.PositionAsync(companyId, number)).Value.Outstanding.Should().Contain("Negotiation broken off: Owner withdrew.");
        (await land.OfferAsync(companyId, number, NegotiationParty.Council, 11_000m, new DateOnly(2026, 3, 3), null)).Error.Code.Should().Be("Hse.NegotiationClosed");
    }
}
