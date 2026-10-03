using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Banking;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers bank transfers: 500 between two USD accounts is a single journal; 1,100 USD sent that arrives as 980 EUR at
/// the 1.10 daily rate is worth 1,078, so 22 is a realised exchange loss and the clearing account is left at nil;
/// transfers to the same account, of nothing, or across currencies without what arrived, are refused and post nothing.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class TreasuryTransferTests
{
    private readonly AccountingFixture _fixture;

    public TreasuryTransferTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Transfers_move_money_and_realise_exchange_differences()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "1290", "Savings Account", AccountType.Asset));
        db.Accounts.Add(new LedgerAccount(companyId, "1490", "EUR Account", AccountType.Asset));
        db.Accounts.Add(new LedgerAccount(companyId, "2490", "Transfer Clearing", AccountType.Liability));
        BankAccount current = new(companyId, "TR-CUR", "Current", Scenario.Usd, "1100");
        BankAccount savings = new(companyId, "TR-SAV", "Savings", Scenario.Usd, "1290");
        BankAccount euro = new(companyId, "TR-EUR", "Euro account", Scenario.Eur, "1490");
        db.Set<BankAccount>().AddRange(current, savings, euro);
        await db.SaveChangesAsync();

        ITreasuryService treasury = scope.ServiceProvider.GetRequiredService<ITreasuryService>();
        BankTransferRequest Transfer(BankAccount from, BankAccount to, decimal sent, decimal? received, string reference) => new()
        {
            CompanyId = companyId, FromBankAccountId = from.Id, ToBankAccountId = to.Id, Date = new DateOnly(2026, 3, 10), AmountSent = sent, AmountReceived = received,
            Reference = reference, ClearingAccountCode = "2490", FxGainAccountCode = "7100", FxLossAccountCode = "7200",
        };

        (await treasury.TransferAsync(Transfer(current, savings, 500m, null, "TR-1"))).Value.JournalNumbers.Should().HaveCount(1);

        BankTransferResult fx = (await treasury.TransferAsync(Transfer(current, euro, 1_100m, 980m, "TR-2"))).Value;
        (fx.JournalNumbers.Count, fx.BaseSent, fx.BaseReceived, fx.FxGainLoss).Should().Be((3, 1_100m, 1_078m, -22m));

        (await treasury.TransferAsync(Transfer(current, current, 10m, null, "TR-X"))).Error.Code.Should().Be("Treasury.Invalid");
        (await treasury.TransferAsync(Transfer(current, savings, 0m, null, "TR-X"))).Error.Code.Should().Be("Treasury.Invalid");
        (await treasury.TransferAsync(Transfer(current, euro, 10m, null, "TR-X"))).Error.Code.Should().Be("Treasury.CrossCurrency");
        (await db.Journals.CountAsync(j => j.CompanyId == companyId && j.DocumentNumber == "TR-X")).Should().Be(0);

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 3, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Single(r => r.AccountCode == "1100").Credit.Should().Be(1_600m);
        tb.Rows.Single(r => r.AccountCode == "1290").Debit.Should().Be(500m);
        tb.Rows.Single(r => r.AccountCode == "1490").Debit.Should().Be(1_078m);
        tb.Rows.Single(r => r.AccountCode == "7200").Debit.Should().Be(22m);
        tb.Rows.Should().NotContain(r => r.AccountCode == "2490" && (r.Debit != 0m || r.Credit != 0m));
    }
}
