using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class UnallocationTests
{
    private readonly AccountingFixture _fixture;

    public UnallocationTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Unallocate_restores_both_items_and_marks_the_allocation_reversed()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer c = new(companyId, "UA-1", "Un-allocate Co", Scenario.Usd);
            db.Customers.Add(c);
            await db.SaveChangesAsync();
            customerId = c.Id;
        }

        long invoiceId;
        long receiptId;
        long allocationId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            IAllocationService alloc = scope.ServiceProvider.GetRequiredService<IAllocationService>();

            var inv = await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = customerId, DocumentNumber = "INV-U1", DocumentDate = new DateOnly(2026, 1, 1), Currency = Scenario.Usd, NetAmount = 400m, SalesAccountCode = "4000" });
            invoiceId = inv.Value.OpenItemId;
            var rcpt = await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = customerId, DocumentNumber = "RCT-U1", ReceiptDate = new DateOnly(2026, 1, 10), Currency = Scenario.Usd, Amount = 300m, BankAccountCode = "1100" });
            receiptId = rcpt.Value.OpenItemId;

            var a = await alloc.AllocateAsync(companyId, TradingLedger.AccountsReceivable, invoiceId, receiptId, 300m, new DateOnly(2026, 1, 10));
            a.IsSuccess.Should().BeTrue(a.IsFailure ? a.Error.ToString() : null);
            allocationId = a.Value.AllocationId;
        }

        // After allocation the debtor owes 100.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            (await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 1, 31))).Total.Should().Be(100m);
        }

        // Un-allocate: both items return to their pre-allocation state.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IAllocationService alloc = scope.ServiceProvider.GetRequiredService<IAllocationService>();
            var un = await alloc.UnallocateAsync(companyId, allocationId, new DateOnly(2026, 1, 20));
            un.IsSuccess.Should().BeTrue(un.IsFailure ? un.Error.ToString() : null);
            un.Value.RestoredTransaction.Should().Be(300m);
            un.Value.ReversedFxBase.Should().Be(0m);

            // Reversing again is refused.
            var again = await alloc.UnallocateAsync(companyId, allocationId, new DateOnly(2026, 1, 20));
            again.IsFailure.Should().BeTrue();
            again.Error.Code.Should().Be("Alloc.AlreadyReversed");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            decimal invoiceOutstanding = await db.Set<OpenItem>().Where(i => i.Id == invoiceId).Select(i => i.OutstandingTransaction).FirstAsync();
            decimal receiptOutstanding = await db.Set<OpenItem>().Where(i => i.Id == receiptId).Select(i => i.OutstandingTransaction).FirstAsync();
            invoiceOutstanding.Should().Be(400m); // fully restored
            receiptOutstanding.Should().Be(300m); // credit available again

            bool reversed = await db.Set<Allocation>().Where(a => a.Id == allocationId).Select(a => a.IsReversed).FirstAsync();
            reversed.Should().BeTrue();

            // Net balance is unchanged (invoice 400 debit + receipt 300 credit = 100); the point is the items
            // are unmatched again, so the receipt is free to be reversed or re-allocated.
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            (await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 1, 31))).Total.Should().Be(100m);
        }
    }

    [SkippableFact]
    public async Task Unallocate_reverses_realised_fx()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer c = new(companyId, "UA-FX", "FX Debtor", Scenario.Eur);
            db.Customers.Add(c);
            await db.SaveChangesAsync();
            customerId = c.Id;
        }

        long allocationId;
        decimal realisedFx;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            IAllocationService alloc = scope.ServiceProvider.GetRequiredService<IAllocationService>();

            // EUR invoice at 1.10, EUR receipt at 1.20 → realised FX on the base difference.
            var inv = await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = customerId, DocumentNumber = "EINV", DocumentDate = new DateOnly(2026, 1, 1), Currency = Scenario.Eur, NetAmount = 100m, SalesAccountCode = "4000", ExchangeRate = 1.10m });
            var rcpt = await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = customerId, DocumentNumber = "ERCT", ReceiptDate = new DateOnly(2026, 2, 1), Currency = Scenario.Eur, Amount = 100m, BankAccountCode = "1150", ExchangeRate = 1.20m });

            var a = await alloc.AllocateAsync(companyId, TradingLedger.AccountsReceivable, inv.Value.OpenItemId, rcpt.Value.OpenItemId, 100m, new DateOnly(2026, 2, 1));
            a.IsSuccess.Should().BeTrue(a.IsFailure ? a.Error.ToString() : null);
            a.Value.RealisedFxBase.Should().NotBe(0m);
            allocationId = a.Value.AllocationId;
            realisedFx = a.Value.RealisedFxBase;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IAllocationService alloc = scope.ServiceProvider.GetRequiredService<IAllocationService>();
            var un = await alloc.UnallocateAsync(companyId, allocationId, new DateOnly(2026, 2, 10));
            un.IsSuccess.Should().BeTrue(un.IsFailure ? un.Error.ToString() : null);
            un.Value.ReversedFxBase.Should().Be(-realisedFx);
            un.Value.FxReversalJournalNumber.Should().NotBeNull();

            // The FX gain/loss nets to nil after reversal, and the ledger still balances.
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 2, 28));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Where(r => r.AccountCode is "7100" or "7200").Sum(r => r.Debit - r.Credit).Should().Be(0m);
        }
    }
}
