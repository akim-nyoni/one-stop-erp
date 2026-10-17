using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Accounting;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the Data Health dashboard: a clean company is all OK; then a deferred journal shows as unposted (warning), a
/// system journal into the receivables control account shows as a control variance (error), a deleted customer still owing
/// shows as an orphan (warning), and a stock balance below zero shows as negative stock (error) - which the integrity check
/// also catches - and the dashboard is graded by its worst indicator.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class DataHealthTests
{
    private readonly AccountingFixture _fixture;

    public DataHealthTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task The_dashboard_finds_each_kind_of_problem()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IDataHealthService health = scope.ServiceProvider.GetRequiredService<IDataHealthService>();
        DateOnly asAt = new(2026, 6, 30);

        DataHealthReport clean = (await health.SnapshotAsync(companyId, asAt)).Value;
        clean.Overall.Should().Be(HealthStatus.Ok);
        clean.Indicators.Should().HaveCount(6).And.OnlyContain(i => i.Status == HealthStatus.Ok && i.Count == 0);

        // Unposted: a deferred journal.
        IPostingEngine posting = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        (await posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId, PostingDate = new DateOnly(2026, 6, 1), TransactionCurrency = Scenario.Usd, Narrative = "Accrual awaiting review", Deferred = true,
            Lines = [new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Debit, TransactionAmount = 75m }, new JournalLineRequest { AccountCode = "4400", Side = JournalSide.Credit, TransactionAmount = 75m }],
        })).IsSuccess.Should().BeTrue();

        // Control variance: a system journal straight into the receivables control account.
        (await posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId, PostingDate = new DateOnly(2026, 6, 2), TransactionCurrency = Scenario.Usd, Narrative = "Mis-posted to AR control",
            Lines = [new JournalLineRequest { AccountCode = "1200", Side = JournalSide.Debit, TransactionAmount = 100m }, new JournalLineRequest { AccountCode = "4400", Side = JournalSide.Credit, TransactionAmount = 100m }],
        })).IsSuccess.Should().BeTrue();

        // Orphan: a customer deleted while still owing.
        Customer gone = new(companyId, "DH-GONE", "Deleted Debtor", Scenario.Usd);
        db.Customers.Add(gone);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = gone.Id, DocumentNumber = $"DH-INV-{companyId}", DocumentDate = new DateOnly(2026, 6, 3), Currency = Scenario.Usd, NetAmount = 250m, SalesAccountCode = "4400",
        })).IsSuccess.Should().BeTrue();
        gone.IsDeleted = true;
        await db.SaveChangesAsync();

        // Negative stock, forced behind the system's back.
        Item item = new(companyId, $"DH-ITEM-{companyId}", "Cement");
        Warehouse warehouse = new(companyId, $"DH-WH-{companyId}", "Yard");
        db.Items.Add(item);
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync();
        db.StockBalances.Add(new StockBalance(companyId, item.Id, warehouse.Id));
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE inv.stock_balance SET quantity_on_hand = -2, average_cost = 10 WHERE company_id = {companyId} AND item_id = {item.Id}");

        DataHealthReport report = (await health.SnapshotAsync(companyId, asAt)).Value;
        report.Overall.Should().Be(HealthStatus.Error);
        Dictionary<HealthIndicatorKind, HealthIndicator> byKind = report.Indicators.ToDictionary(i => i.Kind);
        (byKind[HealthIndicatorKind.UnpostedBatches].Status, byKind[HealthIndicatorKind.UnpostedBatches].Count, byKind[HealthIndicatorKind.UnpostedBatches].Value).Should().Be((HealthStatus.Warning, 1, 75m));
        byKind[HealthIndicatorKind.UnpostedBatches].Items.Single().Should().Contain("deferred").And.Contain("Accrual awaiting review");
        (byKind[HealthIndicatorKind.ControlVariances].Status, byKind[HealthIndicatorKind.ControlVariances].Count).Should().Be((HealthStatus.Error, 1));
        byKind[HealthIndicatorKind.ControlVariances].Items.Single().Should().Contain("1200");
        (byKind[HealthIndicatorKind.OrphanRecords].Status, byKind[HealthIndicatorKind.OrphanRecords].Count, byKind[HealthIndicatorKind.OrphanRecords].Value).Should().Be((HealthStatus.Warning, 1, 250m));
        (byKind[HealthIndicatorKind.NegativeStock].Status, byKind[HealthIndicatorKind.NegativeStock].Count, byKind[HealthIndicatorKind.NegativeStock].Value).Should().Be((HealthStatus.Error, 1, 20m));
        byKind[HealthIndicatorKind.UnbalancedBatches].Status.Should().Be(HealthStatus.Ok);
        byKind[HealthIndicatorKind.LedgerIntegrity].Items.Should().Contain(i => i.StartsWith("StockQuantity", StringComparison.Ordinal));
    }
}
