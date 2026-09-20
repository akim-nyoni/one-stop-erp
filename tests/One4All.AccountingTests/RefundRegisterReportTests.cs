using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Receipting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;
using ReceiptRequest = One4All.Application.Abstractions.Receipting.ReceiptRequest;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RefundRegisterReportTests
{
    private const long Cashier = 9501;
    private const long Approver = 9502;

    private readonly AccountingFixture _fixture;

    public RefundRegisterReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Paid_refund_appears_on_the_refunds_register()
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
            await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
            Customer c = new(companyId, "REFC", "Refund Customer", Scenario.Usd);
            db.Customers.Add(c);
            CashierStation station = new(companyId, "TILL-RF", "Counter RF", "RF");
            db.Set<CashierStation>().Add(station);
            await db.SaveChangesAsync();
            customerId = c.Id;
            stationId = station.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
            long sessionId = (await rcp.OpenSessionAsync(companyId, stationId, Cashier, new DateOnly(2026, 2, 1), [new FloatDeclaration(Scenario.Usd, 0m)])).Value;
            // Overpay 150 -> credit on account.
            (await rcp.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, SessionId = sessionId, PayerKind = PayerKind.ArCustomer, PayerAccountId = customerId, Date = new DateOnly(2026, 2, 1), CashAccountCode = "1000",
                Lines = [new ReceiptLineRequest(ReceiptLineType.DebtorPayment, 150m)],
                Tenders = [new ReceiptTenderRequest(TenderType.Cash, 150m)],
            })).IsSuccess.Should().BeTrue();

            var refund = await rcp.RefundCreditAsync(companyId, customerId, 150m, new DateOnly(2026, 2, 5), "1100", Approver, "Duplicate payment");
            refund.IsSuccess.Should().BeTrue(refund.IsFailure ? refund.Error.ToString() : null);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            var result = await runner.RenderAsync(companyId, "RC41", new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 12, 31) }, ReportFormat.Html);
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            string html = Encoding.UTF8.GetString(result.Value.Content);
            html.Should().Contain("Refund Customer").And.Contain("Paid").And.Contain("150.00");
        }
    }
}
