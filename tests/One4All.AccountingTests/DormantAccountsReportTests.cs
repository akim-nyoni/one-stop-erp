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
public sealed class DormantAccountsReportTests
{
    private const long Cashier = 10101;

    private readonly AccountingFixture _fixture;

    public DormantAccountsReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Rc44_lists_debtors_with_no_recent_payment()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long dormantId;
        long activeId;
        long stationId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer dormant = new(companyId, "DORM-A", "Dormant Debtor", Scenario.Usd);
            Customer active = new(companyId, "DORM-B", "Active Debtor", Scenario.Usd);
            db.Customers.AddRange(dormant, active);
            CashierStation station = new(companyId, "TILL-DM", "Counter DM", "DM");
            db.Set<CashierStation>().Add(station);
            await db.SaveChangesAsync();
            dormantId = dormant.Id;
            activeId = active.Id;
            stationId = station.Id;

            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = dormantId, DocumentNumber = "DA", DocumentDate = new DateOnly(2026, 1, 1), Currency = Scenario.Usd, NetAmount = 500m, SalesAccountCode = "4000" });
            await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = activeId, DocumentNumber = "AB", DocumentDate = new DateOnly(2026, 1, 1), Currency = Scenario.Usd, NetAmount = 500m, SalesAccountCode = "4000" });
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
            long sessionId = (await rcp.OpenSessionAsync(companyId, stationId, Cashier, new DateOnly(2026, 9, 1), [new FloatDeclaration(Scenario.Usd, 0m)])).Value;
            // The active debtor paid recently (within 3 months of the as-at date).
            (await rcp.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, SessionId = sessionId, PayerKind = PayerKind.ArCustomer, PayerAccountId = activeId, Date = new DateOnly(2026, 9, 1), CashAccountCode = "1000",
                Lines = [new ReceiptLineRequest(ReceiptLineType.DebtorPayment, 100m)],
                Tenders = [new ReceiptTenderRequest(TenderType.Cash, 100m)],
            })).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            var result = await runner.RenderAsync(companyId, "RC44", new Dictionary<string, object?> { ["AsAtDate"] = new DateOnly(2026, 9, 30) }, ReportFormat.Html);
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            string html = Encoding.UTF8.GetString(result.Value.Content);
            html.Should().Contain("DORM-A").And.Contain("never");   // dormant, no payment
            html.Should().NotContain("DORM-B");                     // paid recently → excluded
        }
    }
}
