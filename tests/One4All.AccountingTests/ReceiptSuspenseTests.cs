using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Receipting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;
using ReceiptRequest = One4All.Application.Abstractions.Receipting.ReceiptRequest;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class ReceiptSuspenseTests
{
    private const long Cashier = 8001;
    private const long Allocator = 8002;

    private readonly AccountingFixture _fixture;

    public ReceiptSuspenseTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Unidentified_money_parks_in_suspense_then_allocates_out_to_the_debtor()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        long stationId;
        string suspenseReceipt;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer resident = new(companyId, "RES-S", "S. Resident", Scenario.Usd);
            db.Customers.Add(resident);
            (await db.Accounts.SingleAsync(a => a.CompanyId == companyId && a.AccountCode == "2600")).MakeControlAccount(ControlAccountType.Suspense);
            CashierStation station = new(companyId, "TILL-S", "Counter S", "RS");
            db.Set<CashierStation>().Add(station);
            await db.SaveChangesAsync();
            customerId = resident.Id;
            stationId = station.Id;
        }

        // The resident owes 400.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = customerId, DocumentNumber = "SB-1", DocumentDate = new DateOnly(2026, 1, 1), Currency = Scenario.Usd, NetAmount = 400m, SalesAccountCode = "4000" });
        }

        // A 300 bank deposit arrives with no matchable reference → parked in suspense.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
            long sessionId = (await rcp.OpenSessionAsync(companyId, stationId, Cashier, new DateOnly(2026, 1, 25), [new FloatDeclaration(Scenario.Usd, 0m)])).Value;
            var suspense = await rcp.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, SessionId = sessionId, PayerKind = PayerKind.Anonymous,
                Date = new DateOnly(2026, 1, 25), CashAccountCode = "1000",
                Lines = [new ReceiptLineRequest(ReceiptLineType.Suspense, 300m, "2600", "Unmatched deposit ref ZX99")],
                Tenders = [new ReceiptTenderRequest(TenderType.BankTransfer, 300m, "ZX99")],
            });
            suspense.IsSuccess.Should().BeTrue(suspense.IsFailure ? suspense.Error.ToString() : null);
            suspenseReceipt = suspense.Value.ReceiptNumber;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 1, 25));
            tb.Rows.Should().Contain(r => r.AccountCode == "2600" && r.Credit == 300m); // sitting in suspense
        }

        // The workbench identifies whose money it is and allocates it out.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
            DateOnly day = new(2026, 1, 26);

            // Only a suspense control account, and no more than it holds unallocated.
            (await rcp.AllocateFromSuspenseAsync(companyId, customerId, 50m, day, "1100", ReceiptAllocationStrategy.OldestFirst, "Not suspense", Allocator)).Error.Code.Should().Be("Rcp.NotSuspense");
            (await rcp.AllocateFromSuspenseAsync(companyId, customerId, 350m, day, "2600", ReceiptAllocationStrategy.OldestFirst, "Too much", Allocator)).Error.Code.Should().Be("Rcp.SuspenseBalance");
            (await rcp.AllocateFromSuspenseAsync(companyId, customerId, 50m, day, "2600", ReceiptAllocationStrategy.OldestFirst, "No such receipt", Allocator, "NOPE-1")).Error.Code
                .Should().Be("Rcp.ReceiptNotFound");

            var moved = await rcp.AllocateFromSuspenseAsync(companyId, customerId, 300m, day, "2600", ReceiptAllocationStrategy.OldestFirst, "Identified as SB-1 payment", Allocator, suspenseReceipt);
            moved.IsSuccess.Should().BeTrue(moved.IsFailure ? moved.Error.ToString() : null);
            moved.Value.AllocatedToDebt.Should().Be(300m);

            var noReason = await rcp.AllocateFromSuspenseAsync(companyId, customerId, 50m, day, "2600", ReceiptAllocationStrategy.OldestFirst, "  ", Allocator);
            noReason.IsFailure.Should().BeTrue();
            noReason.Error.Code.Should().Be("Rcp.ReasonRequired");
        }

        // The allocation records who made it and the unidentified receipt it cleared.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            SuspenseAllocation logged = await db.SuspenseAllocations.AsNoTracking().SingleAsync(a => a.CompanyId == companyId);
            (logged.AllocatedByUserId, logged.ReceiptNumber, logged.Amount).Should().Be(((long?)Allocator, (string?)suspenseReceipt, 300m));
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            AgeingReport debtors = await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 1, 31));
            debtors.Total.Should().Be(100m); // 400 owed − 300 recovered from suspense

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 1, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().NotContain(r => r.AccountCode == "2600" && (r.Debit != 0m || r.Credit != 0m)); // suspense cleared to nil

            // RC30 logs the suspense allocation with its reason.
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            var rc30 = await runner.RenderAsync(companyId, "RC30", new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 12, 31) }, ReportFormat.Html);
            rc30.IsSuccess.Should().BeTrue(rc30.IsFailure ? rc30.Error.ToString() : null);
            System.Text.Encoding.UTF8.GetString(rc30.Value.Content).Should().Contain("Identified as SB-1 payment").And.Contain("300.00");
        }
    }
}
