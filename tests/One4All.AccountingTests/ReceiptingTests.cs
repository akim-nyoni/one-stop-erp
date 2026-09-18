using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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
public sealed class ReceiptingTests
{
    private const long Cashier = 5001;
    private const long Supervisor = 5002;

    private readonly AccountingFixture _fixture;

    public ReceiptingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Receipt_credits_debtor_control_not_income_and_cashup_posts_variance()
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
            Customer resident = new(companyId, "RES-1", "A. Resident", Scenario.Usd);
            db.Customers.Add(resident);
            CashierStation station = new(companyId, "TILL-1", "Front Counter", "RCT");
            db.Set<CashierStation>().Add(station);
            await db.SaveChangesAsync();
            customerId = resident.Id;
            stationId = station.Id;
        }

        // Bill the resident 800 so the payment has a debt to settle (revenue recognised HERE, at billing).
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = customerId, DocumentNumber = "BILL-1", DocumentDate = new DateOnly(2026, 1, 1),
                Currency = Scenario.Usd, NetAmount = 800m, TaxAmount = 0m, SalesAccountCode = "4000",
            });
        }

        long sessionId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
            var open = await rcp.OpenSessionAsync(companyId, stationId, Cashier, new DateOnly(2026, 1, 20), [new FloatDeclaration(Scenario.Usd, 100m)]);
            open.IsSuccess.Should().BeTrue(open.IsFailure ? open.Error.ToString() : null);
            sessionId = open.Value;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();

            // Guards: zero receipt and a receipt with neither account nor code are refused.
            var zero = await rcp.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, SessionId = sessionId, Date = new DateOnly(2026, 1, 20), CashAccountCode = "1000",
                Lines = [new ReceiptLineRequest(ReceiptLineType.Sundry, 0m, "4400")], Tenders = [new ReceiptTenderRequest(TenderType.Cash, 0m)],
            });
            zero.IsFailure.Should().BeTrue();

            // Mixed receipt: 500 against the debtor account + 50 sundry income. Cash 550.
            var mixed = await rcp.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, SessionId = sessionId, PayerKind = PayerKind.ArCustomer, PayerAccountId = customerId,
                Date = new DateOnly(2026, 1, 20), CashAccountCode = "1000",
                Lines = [new ReceiptLineRequest(ReceiptLineType.DebtorPayment, 500m), new ReceiptLineRequest(ReceiptLineType.Sundry, 50m, "4400", "Clearance certificate")],
                Tenders = [new ReceiptTenderRequest(TenderType.Cash, 550m)],
            });
            mixed.IsSuccess.Should().BeTrue(mixed.IsFailure ? mixed.Error.ToString() : null);
            mixed.Value.AllocatedToDebt.Should().Be(500m);
            mixed.Value.CreditOnAccount.Should().Be(0m);

            // A deposit receipt: 200 → deposit liability, cash 200.
            var deposit = await rcp.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, SessionId = sessionId, PayerKind = PayerKind.Anonymous,
                Date = new DateOnly(2026, 1, 20), CashAccountCode = "1000",
                Lines = [new ReceiptLineRequest(ReceiptLineType.Deposit, 200m, "2500", "Hall hire deposit")],
                Tenders = [new ReceiptTenderRequest(TenderType.Cash, 200m)],
            });
            deposit.IsSuccess.Should().BeTrue(deposit.IsFailure ? deposit.Error.ToString() : null);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
            // Blind cash-up: expected = float 100 + cash 750 = 850; counted 840 → shortage 10.
            var close = await rcp.CloseSessionAsync(companyId, sessionId, new DateOnly(2026, 1, 20), [new FloatDeclaration(Scenario.Usd, 840m)], "1000", "7500");
            close.IsSuccess.Should().BeTrue(close.IsFailure ? close.Error.ToString() : null);
            close.Value.ExpectedCashBase.Should().Be(850m);
            close.Value.VarianceBase.Should().Be(-10m);

            // SoD: the cashier cannot verify their own cash-up; a supervisor can.
            var self = await rcp.VerifySessionAsync(companyId, sessionId, Cashier);
            self.IsFailure.Should().BeTrue();
            self.Error.Code.Should().Be("Rcp.SelfVerify");
            var verify = await rcp.VerifySessionAsync(companyId, sessionId, Supervisor);
            verify.IsSuccess.Should().BeTrue(verify.IsFailure ? verify.Error.ToString() : null);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            AgeingReport debtors = await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 1, 31));
            debtors.Total.Should().Be(300m); // billed 800 − receipted 500

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 1, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "4400" && r.Credit == 50m);   // sundry income
            tb.Rows.Should().Contain(r => r.AccountCode == "2500" && r.Credit == 200m);  // deposit liability
            tb.Rows.Should().Contain(r => r.AccountCode == "7500" && r.Debit == 10m);    // cash shortage
            tb.Rows.Should().NotContain(r => r.AccountCode == "2600" && (r.Debit != 0m || r.Credit != 0m)); // suspense untouched
        }
    }
}
