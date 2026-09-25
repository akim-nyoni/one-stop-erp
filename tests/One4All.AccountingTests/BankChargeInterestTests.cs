using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Abstractions.Reporting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class BankChargeInterestTests
{
    private readonly AccountingFixture _fixture;

    public BankChargeInterestTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Bank_charge_and_interest_post_against_the_bank()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            IBankingService bank = scope.ServiceProvider.GetRequiredService<IBankingService>();

            (await bank.PostBankChargeAsync(companyId, 0m, new DateOnly(2026, 3, 31), "CHG-0", "1100", "6900")).IsFailure.Should().BeTrue();

            (await bank.PostBankChargeAsync(companyId, 35m, new DateOnly(2026, 3, 31), "CHG-1", "1100", "6900"))
                .IsSuccess.Should().BeTrue();
            (await bank.PostInterestReceivedAsync(companyId, 12m, new DateOnly(2026, 3, 31), "INT-1", "1100", "4800"))
                .IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "6900" && r.Debit == 35m);   // charge expense
            tb.Rows.Should().Contain(r => r.AccountCode == "4800" && r.Credit == 12m);  // interest income
            // Bank net movement: -35 charge + 12 interest = -23 (a credit balance on the bank net).
            tb.Rows.Should().Contain(r => r.AccountCode == "1100" && r.Credit == 23m);
        }
    }
}
