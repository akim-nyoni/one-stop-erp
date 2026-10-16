using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Receipting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Reporting;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// A field collector's handheld numbers receipts from its issued range and stops at its end; the collector's day reconciles on RC47 and the route on RC48.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class FieldCollectionTests
{
    private readonly AccountingFixture _fixture;

    public FieldCollectionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_collector_issues_from_the_range_and_reconciles_the_day()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer stallholder = new(companyId, "FC-STALL", "Market stall 12", Scenario.Usd);
        db.Customers.Add(stallholder);
        CashierStation handheld = new(companyId, "FLD-T", "Collector T", "FT") { IsFieldCollector = true, RouteCode = "R-TEST", ReceiptRangeFrom = 101, ReceiptRangeTo = 102 };
        db.CashierStations.Add(handheld);
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = stallholder.Id, DocumentNumber = "FC-RENT", DocumentDate = new DateOnly(2026, 5, 1), Currency = Scenario.Usd, NetAmount = 90m, SalesAccountCode = "4000",
        });

        IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
        DateOnly day = new(2026, 5, 4);
        long session = Ok(await rcp.OpenSessionAsync(companyId, handheld.Id, 7001, day, [new FloatDeclaration(Scenario.Usd, 0m)]));
        One4All.Application.Abstractions.Receipting.ReceiptRequest Receipt(decimal amount) => new()
        {
            CompanyId = companyId, SessionId = session, PayerKind = PayerKind.ArCustomer, PayerAccountId = stallholder.Id, Date = day, CashAccountCode = "1000",
            Lines = [new ReceiptLineRequest(ReceiptLineType.DebtorPayment, amount)], Tenders = [new ReceiptTenderRequest(TenderType.Cash, amount)],
        };
        Ok(await rcp.PostReceiptAsync(Receipt(30m))).ReceiptNumber.Should().Be("FT-000101");
        Ok(await rcp.PostReceiptAsync(Receipt(20m))).ReceiptNumber.Should().Be("FT-000102");
        (await rcp.PostReceiptAsync(Receipt(10m))).Error.Code.Should().Be("Rcp.RangeExhausted");

        // Back at the office the collector hands in 45 against 50 collected: 5 short.
        CashUpResult closed = Ok(await rcp.CloseSessionAsync(companyId, session, day, [new FloatDeclaration(Scenario.Usd, 45m)], "1000", "7500"));
        closed.VarianceBase.Should().Be(-5m);

        IReportDataProvider reports = scope.ServiceProvider.GetServices<IReportDataProvider>().OfType<ReceiptingReportDataProvider>().Single();
        var daily = await reports.GetRowsAsync(companyId, "RC47", new Dictionary<string, object?> { ["Date"] = day });
        var collector = daily.Single(r => ((string)r["Collector"]!).StartsWith("FLD-T", StringComparison.Ordinal));
        collector["Receipts"].Should().Be(2);
        collector["Value"].Should().Be(50m);
        collector["Variance"].Should().Be(-5m);
        ((string)collector["Collector"]!).Should().Contain("returned");

        var routes = await reports.GetRowsAsync(companyId, "RC48", new Dictionary<string, object?> { ["FromDate"] = day, ["ToDate"] = day });
        var route = routes.Single(r => (string)r["Route"]! == "R-TEST");
        route["Collections"].Should().Be(2);
        route["Amount"].Should().Be(50m);
    }

    private static T Ok<T>(One4All.Core.Common.Result<T> result)
    {
        result.IsSuccess.Should().BeTrue(result.IsFailure ? $"{result.Error.Code}: {result.Error.Message}" : null);
        return result.Value;
    }
}
