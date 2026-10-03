using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Vouchers;
using One4All.Core.Vouchers;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers gift card servicing: a 100 card is part-spent (30) and topped up by 50, leaving 120 on it; reported lost,
/// it is blocked and can no longer be spent or topped up; a replacement takes over its 120 and can be spent; it cannot
/// be replaced twice; both cards' histories read in order.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class GiftCardServicingTests
{
    private readonly AccountingFixture _fixture;

    public GiftCardServicingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Cards_are_topped_up_blocked_and_replaced_with_history()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);

        IVoucherService vouchers = scope.ServiceProvider.GetRequiredService<IVoucherService>();
        IGiftCardServicing cards = scope.ServiceProvider.GetRequiredService<IGiftCardServicing>();
        (await vouchers.IssueGiftCardAsync(new IssueGiftCardRequest
        {
            CompanyId = companyId, CardNumber = "GC-1001", Currency = Scenario.Usd, Value = 100m, IssueDate = new DateOnly(2026, 3, 1), ExpiryDate = new DateOnly(2027, 2, 28),
            CashAccountCode = "1100", LiabilityAccountCode = "2300",
        })).IsSuccess.Should().BeTrue();
        (await vouchers.RedeemAsync(companyId, "GC-1001", 30m)).Value.Balance.Should().Be(70m);
        (await cards.TopUpAsync(companyId, "GC-1001", 50m, new DateOnly(2026, 3, 5), "1100", "2300")).Value.Balance.Should().Be(120m);

        (await cards.BlockAsync(companyId, "GC-1001", new DateOnly(2026, 3, 6), "Reported lost")).Value.Status.Should().Be(GiftCardStatus.Blocked);
        (await vouchers.RedeemAsync(companyId, "GC-1001", 10m)).Error.Code.Should().Be("Voucher.NotActive");
        (await cards.TopUpAsync(companyId, "GC-1001", 10m, new DateOnly(2026, 3, 6), "1100", "2300")).Error.Code.Should().Be("Voucher.CannotTopUp");

        (await cards.ReplaceAsync(companyId, "GC-1001", "GC-2001", new DateOnly(2026, 3, 7))).Value.Balance.Should().Be(120m);
        (await cards.ReplaceAsync(companyId, "GC-1001", "GC-2002", new DateOnly(2026, 3, 7))).Error.Code.Should().Be("Voucher.NotReplaceable");
        (await vouchers.RedeemAsync(companyId, "gc-2001", 20m)).Value.Balance.Should().Be(100m);

        (await cards.HistoryAsync(companyId, "GC-1001")).Value.Select(h => (h.Event, h.Amount, h.BalanceAfter)).Should().Equal(
            (GiftCardEvent.Issued, 100m, 100m),
            (GiftCardEvent.Redeemed, -30m, 70m),
            (GiftCardEvent.ToppedUp, 50m, 120m),
            (GiftCardEvent.Blocked, 0m, 120m),
            (GiftCardEvent.ReplacedOut, -120m, 0m));
        (await cards.HistoryAsync(companyId, "GC-2001")).Value.Select(h => (h.Event, h.Amount, h.BalanceAfter)).Should().Equal(
            (GiftCardEvent.ReplacedIn, 120m, 120m),
            (GiftCardEvent.Redeemed, -20m, 100m));

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 3, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Single(r => r.AccountCode == "2300").Credit.Should().Be(150m); // issue 100 + top-up 50; spends are settled by the sale that takes them
    }
}
