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
public sealed class BackDatedReceiptReportTests
{
    private const long Cashier = 9901;

    private readonly AccountingFixture _fixture;

    public BackDatedReceiptReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Receipt_dated_before_capture_shows_on_rc18()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long stationId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            CashierStation station = new(companyId, "TILL-BD", "Counter BD", "BD");
            db.Set<CashierStation>().Add(station);
            await db.SaveChangesAsync();
            stationId = station.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
            long sessionId = (await rcp.OpenSessionAsync(companyId, stationId, Cashier, new DateOnly(2026, 2, 1), [new FloatDeclaration(Scenario.Usd, 0m)])).Value;
            // Receipt dated well in the past relative to "now" (the captured/CreatedOn date).
            (await rcp.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, SessionId = sessionId, PayerKind = PayerKind.Anonymous, Date = new DateOnly(2026, 2, 1), CashAccountCode = "1000",
                Lines = [new ReceiptLineRequest(ReceiptLineType.Sundry, 75m, "4400", "Late capture")],
                Tenders = [new ReceiptTenderRequest(TenderType.Cash, 75m)],
            })).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            var result = await runner.RenderAsync(companyId, "RC18", new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 12, 31) }, ReportFormat.Html);
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            Encoding.UTF8.GetString(result.Value.Content).Should().Contain("BD-000001").And.Contain("75.00");

            // The 75 cash taken has not been banked.
            var unbanked = await runner.RenderAsync(companyId, "RC27", new Dictionary<string, object?> { ["AsAtDate"] = new DateOnly(2026, 2, 28) }, ReportFormat.Csv);
            unbanked.IsSuccess.Should().BeTrue(unbanked.IsFailure ? unbanked.Error.ToString() : null);
            Encoding.UTF8.GetString(unbanked.Value.Content).Should().Contain("TILL-BD").And.Contain("75");
        }
    }
}
