using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Receipting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;
using ReceiptRequest = One4All.Application.Abstractions.Receipting.ReceiptRequest;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RefundAndBankingTests
{
    private const long Cashier = 9101;
    private const long Approver = 9102;

    private readonly AccountingFixture _fixture;

    public RefundAndBankingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Refund_credit_balance_pays_from_bank_and_clears_the_credit()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        long stationId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer c = new(companyId, "OVR-1", "Overpayer", Scenario.Usd);
            db.Customers.Add(c);
            CashierStation station = new(companyId, "TILL-O", "Counter O", "RO");
            db.Set<CashierStation>().Add(station);
            await db.SaveChangesAsync();
            customerId = c.Id;
            stationId = station.Id;
        }

        // The payer pays 200 with nothing owed → a 200 credit on account.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
            long sessionId = (await rcp.OpenSessionAsync(companyId, stationId, Cashier, new DateOnly(2026, 1, 10), [new FloatDeclaration(Scenario.Usd, 0m)])).Value;
            var receipt = await rcp.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, SessionId = sessionId, PayerKind = PayerKind.ArCustomer, PayerAccountId = customerId,
                Date = new DateOnly(2026, 1, 10), CashAccountCode = "1000",
                Lines = [new ReceiptLineRequest(ReceiptLineType.DebtorPayment, 200m)],
                Tenders = [new ReceiptTenderRequest(TenderType.Cash, 200m)],
            });
            receipt.Value.CreditOnAccount.Should().Be(200m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();

            // Cannot refund more than the available credit.
            var tooMuch = await rcp.RefundCreditAsync(companyId, customerId, 250m, new DateOnly(2026, 1, 15), "1100", Approver, "duplicate payment");
            tooMuch.IsFailure.Should().BeTrue();
            tooMuch.Error.Code.Should().Be("Rcp.InsufficientCredit");

            var refund = await rcp.RefundCreditAsync(companyId, customerId, 200m, new DateOnly(2026, 1, 15), "1100", Approver, "duplicate payment");
            refund.IsSuccess.Should().BeTrue(refund.IsFailure ? refund.Error.ToString() : null);
            refund.Value.Amount.Should().Be(200m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            // No outstanding credit remains on the account.
            decimal credit = await db.Set<OpenItem>()
                .Where(i => i.CompanyId == companyId && i.TraderId == customerId && i.Sign == LedgerSign.Credit)
                .SumAsync(i => i.OutstandingTransaction);
            credit.Should().Be(0m);

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 1, 31));
            tb.IsBalanced.Should().BeTrue();
            // Cash received 200 (till), bank paid out 200 → bank shows −200 (credit).
            tb.Rows.Should().Contain(r => r.AccountCode == "1000" && r.Debit == 200m);
            tb.Rows.Should().Contain(r => r.AccountCode == "1100" && r.Credit == 200m);
        }
    }

    [SkippableFact]
    public async Task Deposit_refund_with_forfeit_releases_liability()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            // Seed a deposit liability directly via a journal: Dr cash 500 / Cr deposits 500.
            IPostingEngine posting = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            await posting.PostAsync(new Application.Abstractions.Accounting.JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 1, 1), TransactionCurrency = Scenario.Usd,
                Lines =
                [
                    new Application.Abstractions.Accounting.JournalLineRequest { AccountCode = "1000", Side = Application.Abstractions.Accounting.JournalSide.Debit, TransactionAmount = 500m },
                    new Application.Abstractions.Accounting.JournalLineRequest { AccountCode = "2500", Side = Application.Abstractions.Accounting.JournalSide.Credit, TransactionAmount = 500m },
                ],
            });
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
            // Refund a 500 deposit, forfeiting 50 (to sundry income 4400) → pay 450.
            var refund = await rcp.RefundDepositAsync(companyId, 500m, 50m, new DateOnly(2026, 2, 1), "2500", "1100", "4400", Approver, "Hall returned with damage");
            refund.IsSuccess.Should().BeTrue(refund.IsFailure ? refund.Error.ToString() : null);
            refund.Value.Amount.Should().Be(450m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 2, 28));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().NotContain(r => r.AccountCode == "2500" && (r.Debit != 0m || r.Credit != 0m)); // liability released
            tb.Rows.Should().Contain(r => r.AccountCode == "4400" && r.Credit == 50m);  // forfeit to income
            tb.Rows.Should().Contain(r => r.AccountCode == "1100" && r.Credit == 450m); // net paid from bank
        }
    }

    [SkippableFact]
    public async Task Banking_moves_cash_through_transit_to_the_bank()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            // Put 1000 in cash on hand: Dr cash 1000 / Cr bank 1000 (a cash withdrawal float).
            IPostingEngine posting = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            await posting.PostAsync(new Application.Abstractions.Accounting.JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 1, 1), TransactionCurrency = Scenario.Usd,
                Lines =
                [
                    new Application.Abstractions.Accounting.JournalLineRequest { AccountCode = "1000", Side = Application.Abstractions.Accounting.JournalSide.Debit, TransactionAmount = 1000m },
                    new Application.Abstractions.Accounting.JournalLineRequest { AccountCode = "1100", Side = Application.Abstractions.Accounting.JournalSide.Credit, TransactionAmount = 1000m },
                ],
            });
        }

        long depositId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IBankingService banking = scope.ServiceProvider.GetRequiredService<IBankingService>();
            var bank = await banking.BankCashAsync(companyId, 1000m, new DateOnly(2026, 1, 20), "DS-001", "1000", "1050");
            bank.IsSuccess.Should().BeTrue(bank.IsFailure ? bank.Error.ToString() : null);
            depositId = bank.Value.DepositId;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 1, 20));
            tb.Rows.Should().Contain(r => r.AccountCode == "1050" && r.Debit == 1000m); // sitting in transit
            // Cash on hand: 1000 in (setup) then 1000 out (to transit) → nets to zero.
            tb.Rows.Should().NotContain(r => r.AccountCode == "1000" && (r.Debit != 0m || r.Credit != 0m));
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IBankingService banking = scope.ServiceProvider.GetRequiredService<IBankingService>();
            var confirm = await banking.ConfirmBankedAsync(companyId, depositId, new DateOnly(2026, 1, 22), "1050", "1100");
            confirm.IsSuccess.Should().BeTrue(confirm.IsFailure ? confirm.Error.ToString() : null);

            var again = await banking.ConfirmBankedAsync(companyId, depositId, new DateOnly(2026, 1, 22), "1050", "1100");
            again.IsFailure.Should().BeTrue();
            again.Error.Code.Should().Be("Rcp.AlreadyBanked");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            BankDeposit deposit = await db.Set<BankDeposit>().FindAsync(depositId) ?? throw new Xunit.Sdk.XunitException("deposit gone");
            deposit.Status.Should().Be(BankDepositStatus.Banked);

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 1, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().NotContain(r => r.AccountCode == "1050" && (r.Debit != 0m || r.Credit != 0m)); // cash-in-transit cleared
        }
    }
}
