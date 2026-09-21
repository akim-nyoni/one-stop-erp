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
public sealed class ReceiptingReportRenderTests
{
    private const long Cashier = 9301;

    private readonly AccountingFixture _fixture;

    public ReceiptingReportRenderTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Wired_rc_reports_render_live_figures()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long stationId;
        long overpayerId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
            Customer overpayer = new(companyId, "OVP", "Over Payer", Scenario.Usd);
            db.Customers.Add(overpayer);
            CashierStation station = new(companyId, "TILL-RPT", "Report Till", "RP");
            db.Set<CashierStation>().Add(station);
            await db.SaveChangesAsync();
            overpayerId = overpayer.Id;
            stationId = station.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
            long sessionId = (await rcp.OpenSessionAsync(companyId, stationId, Cashier, new DateOnly(2026, 3, 10), [new FloatDeclaration(Scenario.Usd, 0m)])).Value;

            // A sundry receipt (income vote 4400) and a deposit (liability 2500).
            (await rcp.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, SessionId = sessionId, PayerKind = PayerKind.Anonymous, Date = new DateOnly(2026, 3, 10), CashAccountCode = "1000",
                Lines = [new ReceiptLineRequest(ReceiptLineType.Sundry, 50m, "4400", "Clearance")],
                Tenders = [new ReceiptTenderRequest(TenderType.Cash, 50m)],
            })).IsSuccess.Should().BeTrue();

            (await rcp.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, SessionId = sessionId, PayerKind = PayerKind.Anonymous, Date = new DateOnly(2026, 3, 10), CashAccountCode = "1000",
                Lines = [new ReceiptLineRequest(ReceiptLineType.Deposit, 200m, "2500", "Hall deposit")],
                Tenders = [new ReceiptTenderRequest(TenderType.MobileMoney, 200m, "MM1")],
            })).IsSuccess.Should().BeTrue();

            // An overpayment → a credit balance on the account.
            (await rcp.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, SessionId = sessionId, PayerKind = PayerKind.ArCustomer, PayerAccountId = overpayerId, Date = new DateOnly(2026, 3, 10), CashAccountCode = "1000",
                Lines = [new ReceiptLineRequest(ReceiptLineType.DebtorPayment, 100m)],
                Tenders = [new ReceiptTenderRequest(TenderType.Cash, 100m)],
            })).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IBankingService banking = scope.ServiceProvider.GetRequiredService<IBankingService>();
            // Bank the cash takings (150 cash on hand) into transit.
            (await banking.BankCashAsync(companyId, 150m, new DateOnly(2026, 3, 11), "DS-RPT", "1000", "1050")).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            var range = new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 3, 31) };
            var asAt = new Dictionary<string, object?> { ["AsAtDate"] = new DateOnly(2026, 3, 31) };

            (await Html(runner, companyId, "RC5", range)).Should().Contain("4400").And.Contain("50.00");                 // collection by vote
            (await Html(runner, companyId, "RC7", range)).Should().Contain("Mobile money").And.Contain("200.00");        // by tender/channel
            string rc12 = await Html(runner, companyId, "RC12", range);                                                  // GL reconciliation
            rc12.Should().Contain("Sundry income").And.Contain("Deposits");
            (await Html(runner, companyId, "RC26", asAt)).Should().Contain("DS-RPT").And.Contain("150.00");              // cash-in-transit ageing
            (await Html(runner, companyId, "RC39", asAt)).Should().Contain("OVP").And.Contain("100.00");                 // credit balances
            (await Html(runner, companyId, "RC51", range)).Should().Contain("2026-03");                                  // collection trend
            (await Html(runner, companyId, "RC17", new())).Should().Contain("RP");                                       // gap analysis (station code)
            (await Html(runner, companyId, "RC10", new() { ["Date"] = new DateOnly(2026, 3, 10) })).Should().Contain(":00").And.Contain("TILL-RPT"); // hourly volume by capture hour
        }
    }

    private static async Task<string> Html(IReportRunner runner, long companyId, string code, Dictionary<string, object?> parameters)
    {
        var result = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Html);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? $"{code}: {result.Error}" : null);
        return Encoding.UTF8.GetString(result.Value.Content);
    }
}
