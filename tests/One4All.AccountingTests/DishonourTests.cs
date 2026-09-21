using System.Text;
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
public sealed class DishonourTests
{
    private const long Cashier = 9701;
    private const long BackOffice = 9702;

    private readonly AccountingFixture _fixture;

    public DishonourTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Dishonoured_cheque_reverses_receipt_raises_penalty_and_lists_on_rc34()
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
            Customer c = new(companyId, "CHQ-1", "Cheque Payer", Scenario.Usd);
            db.Customers.Add(c);
            CashierStation station = new(companyId, "TILL-DH", "Counter DH", "DH");
            db.Set<CashierStation>().Add(station);
            await db.SaveChangesAsync();
            customerId = c.Id;
            stationId = station.Id;

            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = customerId, DocumentNumber = "CHB-1", DocumentDate = new DateOnly(2026, 1, 1), Currency = Scenario.Usd, NetAmount = 400m, SalesAccountCode = "4000" });
        }

        long receiptId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
            long sessionId = (await rcp.OpenSessionAsync(companyId, stationId, Cashier, new DateOnly(2026, 1, 20), [new FloatDeclaration(Scenario.Usd, 0m)])).Value;
            var receipt = await rcp.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, SessionId = sessionId, PayerKind = PayerKind.ArCustomer, PayerAccountId = customerId, Date = new DateOnly(2026, 1, 20), CashAccountCode = "1100",
                Lines = [new ReceiptLineRequest(ReceiptLineType.DebtorPayment, 400m)],
                Tenders = [new ReceiptTenderRequest(TenderType.Cheque, 400m, "CHQ-99887")],
            });
            receiptId = receipt.Value.ReceiptId;
        }

        // Debt cleared by the cheque payment.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            (await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 1, 31))).Total.Should().Be(0m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
            var dishonour = await rcp.RecordDishonourAsync(companyId, receiptId, new DateOnly(2026, 1, 25), penaltyAmount: 25m, penaltyAccountCode: "4400", penaltyInvoiceNumber: "PEN-1", BackOffice, "Cheque returned RD");
            dishonour.IsSuccess.Should().BeTrue(dishonour.IsFailure ? dishonour.Error.ToString() : null);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            // Debt restored (400) + penalty (25) = 425.
            (await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 1, 31))).Total.Should().Be(425m);

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 1, 31));
            tb.IsBalanced.Should().BeTrue();

            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            var rc34 = await runner.RenderAsync(companyId, "RC34", new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 12, 31) }, ReportFormat.Html);
            rc34.IsSuccess.Should().BeTrue(rc34.IsFailure ? rc34.Error.ToString() : null);
            Encoding.UTF8.GetString(rc34.Value.Content).Should().Contain("CHQ-99887").And.Contain("25.00").And.Contain("400.00");
        }
    }
}
