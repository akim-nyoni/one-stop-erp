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
public sealed class ReprintLogTests
{
    private const long Cashier = 9601;

    private readonly AccountingFixture _fixture;

    public ReprintLogTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Reprints_are_numbered_and_logged_on_rc16()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long stationId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            CashierStation station = new(companyId, "TILL-RP", "Counter RP", "RP");
            db.Set<CashierStation>().Add(station);
            await db.SaveChangesAsync();
            stationId = station.Id;
        }

        long receiptId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
            long sessionId = (await rcp.OpenSessionAsync(companyId, stationId, Cashier, new DateOnly(2026, 4, 1), [new FloatDeclaration(Scenario.Usd, 0m)])).Value;
            var receipt = await rcp.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, SessionId = sessionId, PayerKind = PayerKind.Anonymous, Date = new DateOnly(2026, 4, 1), CashAccountCode = "1000",
                Lines = [new ReceiptLineRequest(ReceiptLineType.Sundry, 40m, "4400", "Photocopies")],
                Tenders = [new ReceiptTenderRequest(TenderType.Cash, 40m)],
            });
            receiptId = receipt.Value.ReceiptId;

            (await rcp.ReprintReceiptAsync(companyId, receiptId, new DateOnly(2026, 4, 2), Cashier)).Value.Should().Be(1);
            (await rcp.ReprintReceiptAsync(companyId, receiptId, new DateOnly(2026, 4, 3), Cashier)).Value.Should().Be(2);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            var result = await runner.RenderAsync(companyId, "RC16", new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 12, 31) }, ReportFormat.Html);
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            string html = Encoding.UTF8.GetString(result.Value.Content);
            html.Should().Contain("copy 1").And.Contain("copy 2");
        }
    }
}
