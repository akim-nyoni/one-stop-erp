using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class AgentAndStationeryTests
{
    private const long Cashier = 9201;

    private readonly AccountingFixture _fixture;

    public AgentAndStationeryTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Agent_collects_then_remits_less_commission_and_reconciles()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        long agentId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer resident = new(companyId, "AG-RES", "Agent-served Resident", Scenario.Usd);
            db.Customers.Add(resident);
            await db.SaveChangesAsync();
            customerId = resident.Id;

            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = customerId, DocumentNumber = "AGB", DocumentDate = new DateOnly(2026, 1, 1), Currency = Scenario.Usd, NetAmount = 500m, SalesAccountCode = "4000" });

            IAgentService agents = scope.ServiceProvider.GetRequiredService<IAgentService>();
            agentId = (await agents.RegisterAgentAsync(companyId, "KIOSK1", "Corner Kiosk", 10m)).Value;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IAgentService agents = scope.ServiceProvider.GetRequiredService<IAgentService>();
            var collect = await agents.RecordCollectionAsync(companyId, agentId, customerId, 500m, new DateOnly(2026, 1, 20), "1300");
            collect.IsSuccess.Should().BeTrue(collect.IsFailure ? collect.Error.ToString() : null);
            collect.Value.AllocatedToDebt.Should().Be(500m);
            collect.Value.AgentOutstanding.Should().Be(500m); // held by the agent, not yet remitted
        }

        // The debtor is settled and the money sits in the agent cash control.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            (await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 1, 31))).Total.Should().Be(0m);

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 1, 31));
            tb.Rows.Should().Contain(r => r.AccountCode == "1300" && r.Debit == 500m); // agent holds 500
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IAgentService agents = scope.ServiceProvider.GetRequiredService<IAgentService>();

            // Cannot remit more than the agent holds.
            var over = await agents.RemitAsync(companyId, agentId, 600m, new DateOnly(2026, 1, 25), "1300", "1100", "6200");
            over.IsFailure.Should().BeTrue();
            over.Error.Code.Should().Be("Rcp.OverRemit");

            var remit = await agents.RemitAsync(companyId, agentId, 500m, new DateOnly(2026, 1, 25), "1300", "1100", "6200");
            remit.IsSuccess.Should().BeTrue(remit.IsFailure ? remit.Error.ToString() : null);
            remit.Value.Commission.Should().Be(50m);  // 10% of 500
            remit.Value.Net.Should().Be(450m);
            remit.Value.AgentOutstanding.Should().Be(0m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 1, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().NotContain(r => r.AccountCode == "1300" && (r.Debit != 0m || r.Credit != 0m)); // agent control cleared
            tb.Rows.Should().Contain(r => r.AccountCode == "6200" && r.Debit == 50m);   // commission expensed
            tb.Rows.Should().Contain(r => r.AccountCode == "1100" && r.Debit == 450m);  // net banked
        }
    }

    [SkippableFact]
    public async Task Stationery_book_controls_serials_and_reports_gaps()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
        }

        long bookId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IStationeryService stationery = scope.ServiceProvider.GetRequiredService<IStationeryService>();
            // Book of serials 1001–1005 issued to a cashier.
            bookId = (await stationery.IssueBookAsync(companyId, "BK-01", 1001, 1005, Cashier, new DateOnly(2026, 1, 1))).Value;

            // Capture three manual receipts, leaving a gap at 1003.
            (await stationery.CaptureManualReceiptAsync(companyId, bookId, 1001, 20m, new DateOnly(2026, 1, 2))).IsSuccess.Should().BeTrue();
            (await stationery.CaptureManualReceiptAsync(companyId, bookId, 1002, 35m, new DateOnly(2026, 1, 2))).IsSuccess.Should().BeTrue();
            var capture = await stationery.CaptureManualReceiptAsync(companyId, bookId, 1004, 40m, new DateOnly(2026, 1, 3));
            capture.IsSuccess.Should().BeTrue();
            capture.Value.RemainingUnused.Should().Be(2); // 1003 and 1005

            // A duplicate serial is rejected.
            var dup = await stationery.CaptureManualReceiptAsync(companyId, bookId, 1001, 10m, new DateOnly(2026, 1, 4));
            dup.IsFailure.Should().BeTrue();
            dup.Error.Code.Should().Be("Rcp.SerialRejected");

            // An out-of-range serial is rejected.
            var oor = await stationery.CaptureManualReceiptAsync(companyId, bookId, 1099, 10m, new DateOnly(2026, 1, 4));
            oor.IsFailure.Should().BeTrue();
            oor.Error.Code.Should().Be("Rcp.SerialRejected");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IStationeryService stationery = scope.ServiceProvider.GetRequiredService<IStationeryService>();
            var unused = await stationery.GetUnusedSerialsAsync(companyId, bookId);
            unused.IsSuccess.Should().BeTrue();
            unused.Value.Should().Equal(1003L, 1005L); // the provable gaps
        }
    }
}
