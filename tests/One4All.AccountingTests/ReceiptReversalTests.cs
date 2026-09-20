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
public sealed class ReceiptReversalTests
{
    private const long Cashier = 9001;
    private const long Supervisor = 9002;

    private readonly AccountingFixture _fixture;

    public ReceiptReversalTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Reversing_a_receipt_restores_the_debt_and_undoes_every_line()
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
            Customer resident = new(companyId, "REV-1", "Reversal Resident", Scenario.Usd);
            db.Customers.Add(resident);
            CashierStation station = new(companyId, "TILL-R", "Counter R", "RR");
            db.Set<CashierStation>().Add(station);
            await db.SaveChangesAsync();
            customerId = resident.Id;
            stationId = station.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = customerId, DocumentNumber = "RB-1", DocumentDate = new DateOnly(2026, 1, 1), Currency = Scenario.Usd, NetAmount = 800m, SalesAccountCode = "4000" });
        }

        long receiptId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
            long sessionId = (await rcp.OpenSessionAsync(companyId, stationId, Cashier, new DateOnly(2026, 1, 20), [new FloatDeclaration(Scenario.Usd, 0m)])).Value;
            var receipt = await rcp.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, SessionId = sessionId, PayerKind = PayerKind.ArCustomer, PayerAccountId = customerId,
                Date = new DateOnly(2026, 1, 20), CashAccountCode = "1000",
                Lines = [new ReceiptLineRequest(ReceiptLineType.DebtorPayment, 500m), new ReceiptLineRequest(ReceiptLineType.Sundry, 50m, "4400")],
                Tenders = [new ReceiptTenderRequest(TenderType.Cash, 550m)],
            });
            receipt.IsSuccess.Should().BeTrue(receipt.IsFailure ? receipt.Error.ToString() : null);
            receiptId = receipt.Value.ReceiptId;
        }

        // After the receipt: debtor 300 (800 − 500), sundry income 50.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            (await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 1, 31))).Total.Should().Be(300m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();

            // A cashier cannot reverse their own receipt.
            var self = await rcp.ReverseReceiptAsync(companyId, receiptId, new DateOnly(2026, 1, 21), Cashier, "duplicate");
            self.IsFailure.Should().BeTrue();
            self.Error.Code.Should().Be("Rcp.SelfReverse");

            // A reason is mandatory.
            var noReason = await rcp.ReverseReceiptAsync(companyId, receiptId, new DateOnly(2026, 1, 21), Supervisor, "  ");
            noReason.IsFailure.Should().BeTrue();

            var rev = await rcp.ReverseReceiptAsync(companyId, receiptId, new DateOnly(2026, 1, 21), Supervisor, "Paid to the wrong account");
            rev.IsSuccess.Should().BeTrue(rev.IsFailure ? rev.Error.ToString() : null);
            rev.Value.Amount.Should().Be(550m);

            // Reversing again is refused (the receipt is now cancelled, not deleted).
            var again = await rcp.ReverseReceiptAsync(companyId, receiptId, new DateOnly(2026, 1, 21), Supervisor, "again");
            again.IsFailure.Should().BeTrue();
            again.Error.Code.Should().Be("Rcp.NotReversible");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            Receipt receipt = await db.Set<Receipt>().FindAsync(receiptId) ?? throw new Xunit.Sdk.XunitException("receipt gone");
            receipt.Status.Should().Be(ReceiptStatus.Cancelled); // retained, not deleted
            receipt.CancellationReason.Should().Be("Paid to the wrong account"); // reason captured
            receipt.CancelledByUserId.Should().Be(Supervisor);

            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            (await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 1, 31))).Total.Should().Be(800m); // full debt restored

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 1, 31));
            tb.IsBalanced.Should().BeTrue();
            // Sundry income reversed to nil and cash returned (net zero on 1000 and 4400).
            tb.Rows.Where(r => r.AccountCode == "4400").Sum(r => r.Credit - r.Debit).Should().Be(0m);
            tb.Rows.Where(r => r.AccountCode == "1000").Sum(r => r.Debit - r.Credit).Should().Be(0m);
        }
    }
}
