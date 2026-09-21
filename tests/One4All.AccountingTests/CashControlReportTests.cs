using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Receipting;
using One4All.Infrastructure.Persistence;
using Xunit;
using ReceiptRequest = One4All.Application.Abstractions.Receipting.ReceiptRequest;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class CashControlReportTests
{
    private const long Cashier = 9801;

    private readonly AccountingFixture _fixture;

    public CashControlReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Rc13_reconciles_the_supplied_cash_accounts()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long stationId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            CashierStation station = new(companyId, "TILL-CC", "Counter CC", "CC");
            db.Set<CashierStation>().Add(station);
            await db.SaveChangesAsync();
            stationId = station.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
            long sessionId = (await rcp.OpenSessionAsync(companyId, stationId, Cashier, new DateOnly(2026, 3, 1), [new FloatDeclaration(Scenario.Usd, 0m)])).Value;
            // 300 cash across the counter, then pick up 250 to the safe.
            (await rcp.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, SessionId = sessionId, PayerKind = PayerKind.Anonymous, Date = new DateOnly(2026, 3, 1), CashAccountCode = "1000",
                Lines = [new ReceiptLineRequest(ReceiptLineType.Sundry, 300m, "4400", "Fees")],
                Tenders = [new ReceiptTenderRequest(TenderType.Cash, 300m)],
            })).IsSuccess.Should().BeTrue();
            (await rcp.PickupAsync(companyId, sessionId, 250m, new DateOnly(2026, 3, 1), "1000", "1010", "Drawer exposure")).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            var result = await runner.RenderAsync(companyId, "RC13", new Dictionary<string, object?>
            {
                ["CashAccount"] = "1000", ["SafeAccount"] = "1010", ["TransitAccount"] = "1050", ["BankAccount"] = "1100",
            }, ReportFormat.Html);
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            string html = Encoding.UTF8.GetString(result.Value.Content);
            // Cash on hand: Dr 300 / Cr 250; safe: Dr 250.
            html.Should().Contain("Cash on hand (1000)").And.Contain("Cash in safe (1010)").And.Contain("300.00").And.Contain("250.00");
        }
    }
}
