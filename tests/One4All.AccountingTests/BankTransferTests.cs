using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Abstractions.Reporting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class BankTransferTests
{
    private readonly AccountingFixture _fixture;

    public BankTransferTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Bank_transfer_moves_funds_between_own_accounts()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IBankingService banking = scope.ServiceProvider.GetRequiredService<IBankingService>();

            (await banking.TransferAsync(companyId, 0m, new DateOnly(2026, 3, 1), "X", "1100", "1000")).IsFailure.Should().BeTrue();       // non-positive
            (await banking.TransferAsync(companyId, 100m, new DateOnly(2026, 3, 1), "X", "1100", "1100")).IsFailure.Should().BeTrue();     // same account

            // Move 500 from bank (1100) to cash on hand (1000).
            (await banking.TransferAsync(companyId, 500m, new DateOnly(2026, 3, 1), "XFER-1", "1100", "1000")).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "1000" && r.Debit == 500m);   // cash on hand up
            tb.Rows.Should().Contain(r => r.AccountCode == "1100" && r.Credit == 500m);  // bank down
        }
    }
}
