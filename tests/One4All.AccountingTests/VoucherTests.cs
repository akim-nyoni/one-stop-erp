using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Vouchers;
using One4All.Core.Vouchers;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class VoucherTests
{
    private readonly AccountingFixture _fixture;

    public VoucherTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Gift_card_issue_redeem_and_breakage()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            companyId = await Scenario.SeedAsync(scope.ServiceProvider.GetRequiredService<One4AllDbContext>());
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IVoucherService vouchers = scope.ServiceProvider.GetRequiredService<IVoucherService>();

            // Issue a 50 card: Dr cash 1000, Cr gift-card liability 2300.
            var issued = await vouchers.IssueGiftCardAsync(new IssueGiftCardRequest
            {
                CompanyId = companyId, CardNumber = "GC-001", Currency = Scenario.Usd, Value = 50m, IssueDate = new DateOnly(2026, 3, 1),
                CashAccountCode = "1000", LiabilityAccountCode = "2300", ExpiryDate = new DateOnly(2026, 6, 30),
            });
            issued.IsSuccess.Should().BeTrue(issued.IsFailure ? issued.Error.ToString() : null);
            issued.Value.Balance.Should().Be(50m);

            // Redeem 20 -> balance 30.
            (await vouchers.RedeemAsync(companyId, "GC-001", 20m)).Value.Balance.Should().Be(30m);

            // Over-redeem is rejected.
            var over = await vouchers.RedeemAsync(companyId, "GC-001", 40m);
            over.IsFailure.Should().BeTrue();
            over.Error.Code.Should().Be("Voucher.InsufficientBalance");

            // Recognise the residual 30 as breakage: Dr liability 2300, Cr breakage income 4900.
            var breakage = await vouchers.RecogniseBreakageAsync(companyId, "GC-001", new DateOnly(2026, 7, 1), "2300", "4900");
            breakage.IsSuccess.Should().BeTrue(breakage.IsFailure ? breakage.Error.ToString() : null);
            breakage.Value.Balance.Should().Be(0m);
            breakage.Value.Status.Should().Be(GiftCardStatus.Expired);
        }
    }
}
