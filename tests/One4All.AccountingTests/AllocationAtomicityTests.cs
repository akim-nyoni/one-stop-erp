using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers allocation when its realised FX cannot be posted (the AR period is closed): the allocation fails without
/// touching the items, so even a later save in the same scope persists nothing - check data stays clean - and once
/// the period is reopened the same allocation succeeds with its realised gain.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AllocationAtomicityTests
{
    private readonly AccountingFixture _fixture;

    public AllocationAtomicityTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_refused_realised_fx_leaves_the_items_untouched()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long yearId;
        long invoice;
        long receipt;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).SingleAsync();
            Customer customer = new(companyId, "AT-C", "Euro Customer", Scenario.Eur);
            db.Customers.Add(customer);
            await db.SaveChangesAsync();

            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();

            // EUR 500 invoiced at 1.10 (USD 550) and received at 1.20 (USD 600): allocating realises a 50 gain.
            invoice = (await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "AT-I", DocumentDate = new DateOnly(2026, 3, 10), Currency = Scenario.Eur, NetAmount = 500m, SalesAccountCode = "4000" })).Value.OpenItemId;
            receipt = (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "AT-R", ReceiptDate = new DateOnly(2026, 4, 20), Currency = Scenario.Eur, Amount = 500m, ExchangeRate = 1.20m, BankAccountCode = "1150" })).Value.OpenItemId;

            (await scope.ServiceProvider.GetRequiredService<IPeriodService>().SetStatusAsync(companyId, yearId, 5, "AR", PeriodStatus.Closed)).IsSuccess.Should().BeTrue();
        }

        DateOnly may5 = new(2026, 5, 5);
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            Result<AllocationResult> refused = await scope.ServiceProvider.GetRequiredService<IAllocationService>()
                .AllocateAsync(companyId, TradingLedger.AccountsReceivable, invoice, receipt, 500m, may5);
            refused.IsFailure.Should().BeTrue();

            // Whatever else this scope saves, the failed allocation leaves no trace.
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.OpenItems.AsNoTracking().SingleAsync(i => i.Id == invoice)).OutstandingTransaction.Should().Be(500m);
            (await db.OpenItems.AsNoTracking().SingleAsync(i => i.Id == receipt)).OutstandingTransaction.Should().Be(500m);
            (await db.Allocations.CountAsync(a => a.CompanyId == companyId)).Should().Be(0);
            (await scope.ServiceProvider.GetRequiredService<ILedgerIntegrityService>().CheckAsync(companyId)).Value.Issues.Should().BeEmpty();

            // Reopened, the allocation goes through and realises the gain.
            (await scope.ServiceProvider.GetRequiredService<IPeriodService>().SetStatusAsync(companyId, yearId, 5, "AR", PeriodStatus.Open)).IsSuccess.Should().BeTrue();
            Result<AllocationResult> allocated = await scope.ServiceProvider.GetRequiredService<IAllocationService>()
                .AllocateAsync(companyId, TradingLedger.AccountsReceivable, invoice, receipt, 500m, may5);
            allocated.IsSuccess.Should().BeTrue(allocated.IsFailure ? allocated.Error.ToString() : null);
            allocated.Value.RealisedFxBase.Should().Be(50m);
            (await scope.ServiceProvider.GetRequiredService<ILedgerIntegrityService>().CheckAsync(companyId)).Value.Issues.Should().BeEmpty();
        }
    }
}
