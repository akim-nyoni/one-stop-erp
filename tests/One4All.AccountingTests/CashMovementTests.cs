using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Receipting;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;
using ReceiptRequest = One4All.Application.Abstractions.Receipting.ReceiptRequest;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class CashMovementTests
{
    private const long Cashier = 9401;

    private readonly AccountingFixture _fixture;

    public CashMovementTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Pickup_and_petty_cash_reduce_the_drawer_and_appear_on_rc28()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long stationId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
            CashierStation station = new(companyId, "TILL-CM", "Counter CM", "CM");
            db.Set<CashierStation>().Add(station);
            await db.SaveChangesAsync();
            stationId = station.Id;
        }

        long sessionId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
            sessionId = (await rcp.OpenSessionAsync(companyId, stationId, Cashier, new DateOnly(2026, 3, 10), [new FloatDeclaration(Scenario.Usd, 100m)])).Value;

            // Take 300 cash across the counter.
            (await rcp.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, SessionId = sessionId, PayerKind = PayerKind.Anonymous, Date = new DateOnly(2026, 3, 10), CashAccountCode = "1000",
                Lines = [new ReceiptLineRequest(ReceiptLineType.Sundry, 300m, "4400", "Fees")],
                Tenders = [new ReceiptTenderRequest(TenderType.Cash, 300m)],
            })).IsSuccess.Should().BeTrue();

            // Pick up 250 to the safe, and pay 20 petty cash. Reason is mandatory.
            (await rcp.PickupAsync(companyId, sessionId, 250m, new DateOnly(2026, 3, 10), "1000", "1010", "  ")).IsFailure.Should().BeTrue();
            (await rcp.PickupAsync(companyId, sessionId, 250m, new DateOnly(2026, 3, 10), "1000", "1010", "Drawer exposure")).IsSuccess.Should().BeTrue();
            (await rcp.PettyCashPayoutAsync(companyId, sessionId, 20m, new DateOnly(2026, 3, 10), "1000", "6300", "Milk for the office")).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
            // Expected drawer = float 100 + cash 300 - pickup 250 - petty cash 20 = 130.
            var close = await rcp.CloseSessionAsync(companyId, sessionId, new DateOnly(2026, 3, 10), [new FloatDeclaration(Scenario.Usd, 130m)], "1000", "7500");
            close.IsSuccess.Should().BeTrue(close.IsFailure ? close.Error.ToString() : null);
            close.Value.ExpectedCashBase.Should().Be(130m);
            close.Value.VarianceBase.Should().Be(0m); // counted matches → no shortage
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "1010" && r.Debit == 250m);  // safe
            tb.Rows.Should().Contain(r => r.AccountCode == "6300" && r.Debit == 20m);   // petty cash expense

            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            var result = await runner.RenderAsync(companyId, "RC28", new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 12, 31) }, ReportFormat.Html);
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            string html = Encoding.UTF8.GetString(result.Value.Content);
            html.Should().Contain("Pickup").And.Contain("250.00").And.Contain("Milk for the office");
        }
    }
}
