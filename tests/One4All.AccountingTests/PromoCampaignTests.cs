using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Vouchers;
using One4All.Core.Vouchers;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers promotion campaigns: a March campaign of single-use codes giving 10% off a spend of 50 or more, capped at two
/// redemptions; a quote does not use the code; a code is refused below the minimum spend, outside March, a second time,
/// and once the campaign has been redeemed twice; unknown and duplicate codes are refused; the analysis reports codes
/// issued and used, spend and discount.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PromoCampaignTests
{
    private readonly AccountingFixture _fixture;

    public PromoCampaignTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Codes_are_checked_redeemed_and_analysed()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);

        IPromoService promos = scope.ServiceProvider.GetRequiredService<IPromoService>();
        PromoCampaignRequest Campaign(string code, IReadOnlyList<string> codes) => new(code, "Spring sale", PromoDiscountType.Percent, 10m,
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), 50m, 1, 2, codes);
        long campaignId = (await promos.CreateCampaignAsync(companyId, Campaign("SPRING", ["SPR-001", "SPR-002", "spr-003"]))).Value;
        (await promos.CreateCampaignAsync(companyId, Campaign("SPRING2", ["SPR-001"]))).Error.Code.Should().Be("Promo.DuplicateCode");

        DateOnly march = new(2026, 3, 15);
        (await promos.QuoteAsync(companyId, "spr-001", march, 85.55m)).Value.Discount.Should().Be(8.56m);
        (await promos.QuoteAsync(companyId, "SPR-001", march, 40m)).Error.Code.Should().Be("Promo.MinimumSpend");
        (await promos.QuoteAsync(companyId, "SPR-001", new DateOnly(2026, 4, 1), 85m)).Error.Code.Should().Be("Promo.OutOfDates");
        (await promos.QuoteAsync(companyId, "NOPE", march, 85m)).Error.Code.Should().Be("Promo.Unknown");

        (await promos.RedeemAsync(companyId, "SPR-001", march, 85.55m, "POS-1001")).Value.Discount.Should().Be(8.56m);
        (await promos.RedeemAsync(companyId, "SPR-001", march, 60m, "POS-1002")).Error.Code.Should().Be("Promo.CodeUsed");
        (await promos.RedeemAsync(companyId, "SPR-002", march, 200m, "POS-1003")).Value.Discount.Should().Be(20m);
        (await promos.RedeemAsync(companyId, "SPR-003", march, 100m, "POS-1004")).Error.Code.Should().Be("Promo.CampaignExhausted");

        (await promos.AnalysisAsync(companyId, campaignId)).Value.Should().Be(new PromoAnalysis("SPRING", 3, 2, 2, 285.55m, 28.56m));
    }
}
