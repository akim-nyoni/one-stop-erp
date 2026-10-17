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
public sealed class SodViolationReportTests
{
    private const long Cashier = 10401;

    private readonly AccountingFixture _fixture;

    public SodViolationReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Blocked_self_verify_is_logged_on_rc22()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long stationId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            CashierStation station = new(companyId, "TILL-SD", "Counter SD", "SD");
            db.Set<CashierStation>().Add(station);
            await db.SaveChangesAsync();
            stationId = station.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
            long sessionId = (await rcp.OpenSessionAsync(companyId, stationId, Cashier, new DateOnly(2026, 3, 1), [new FloatDeclaration(Scenario.Usd, 0m)])).Value;
            await rcp.CloseSessionAsync(companyId, sessionId, new DateOnly(2026, 3, 1), [new FloatDeclaration(Scenario.Usd, 0m)], "1000", "7500");
            // The cashier tries to verify their own cash-up - refused and logged.
            var self = await rcp.VerifySessionAsync(companyId, sessionId, Cashier);
            self.IsFailure.Should().BeTrue();
            self.Error.Code.Should().Be("Rcp.SelfVerify");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            var result = await runner.RenderAsync(companyId, "RC22", new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 12, 31) }, ReportFormat.Html);
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            string html = Encoding.UTF8.GetString(result.Value.Content);
            html.Should().Contain($"#{Cashier}").And.Contain("verify own cash-up");
        }
    }
}
