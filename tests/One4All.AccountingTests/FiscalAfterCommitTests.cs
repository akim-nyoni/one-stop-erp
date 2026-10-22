using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Scheduling;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers signing with the fiscal device only once a posting has committed: a unit of work attempted again after a lost race has its
/// invoice signed once, not once an attempt; an invoice and a credit note the caller rolls back are never signed (the revenue authority
/// holds no signed document that does not exist); and a batch of invoices (a billing run) is queued rather than signed while it posts,
/// then signed - with its tax - by the ar.fiscal-invoices task.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class FiscalAfterCommitTests
{
    private readonly AccountingFixture _fixture;

    public FiscalAfterCommitTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task An_invoice_whose_unit_is_retried_after_a_lost_race_is_signed_once()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        SimulatedFiscalDeviceProvider device = new();
        DeadlockOnce deadlock = new("FAC-RACE1");
        await using ServiceProvider provider = _fixture.BuildServiceProvider(s =>
        {
            s.AddSingleton<IFiscalDeviceProvider>(device);
            s.ConfigureDbContext<One4AllDbContext>(o => o.AddInterceptors(deadlock));
        });
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer buyer = new(companyId, "FAC-C1", "Race Buyer", Scenario.Usd);
        db.Customers.Add(buyer);
        await db.SaveChangesAsync();

        // The first attempt is ended by the database where it saves the invoice (as when two postings deadlock); the unit runs again.
        var invoice = await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostInvoiceAsync(Invoice(companyId, buyer.Id, "FAC-RACE1"));
        invoice.IsSuccess.Should().BeTrue(invoice.IsFailure ? invoice.Error.Message : null);
        deadlock.Thrown.Should().BeTrue("the first attempt lost the race");

        device.Signed.Where(r => r.DocumentNumber == "FAC-RACE1").Should().ContainSingle("the device signs the invoice once, after the attempt that committed");
        OpenItem signed = await db.OpenItems.AsNoTracking().SingleAsync(i => i.Id == invoice.Value.OpenItemId);
        (signed.FiscalQueued, signed.FiscalCode).Should().Be((false, "SIM-0001-000001"));
    }

    [SkippableFact]
    public async Task Documents_rolled_back_after_posting_are_never_signed()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        SimulatedFiscalDeviceProvider device = new();
        await using ServiceProvider provider = _fixture.BuildServiceProvider(s => s.AddSingleton<IFiscalDeviceProvider>(device));
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer buyer = new(companyId, "FAC-C2", "Rolled Back Buyer", Scenario.Usd) { TaxRegistration = "VAT-91" };
        db.Customers.Add(buyer);
        await db.SaveChangesAsync();
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();

        // The caller's own transaction posts an invoice and a credit note against it - past the point where they used to be signed - and then fails.
        await using (IDbContextTransaction transaction = await db.Database.BeginTransactionAsync())
        {
            (await ar.PostInvoiceAsync(Invoice(companyId, buyer.Id, "FAC-RB1"))).IsSuccess.Should().BeTrue();
            (await ar.PostCreditNoteAsync(new SalesCreditNoteRequest
            {
                CompanyId = companyId, CustomerId = buyer.Id, DocumentNumber = "FAC-RBC1", DocumentDate = new DateOnly(2026, 6, 3), Currency = Scenario.Usd, NetAmount = 20m, TaxAmount = 3m,
                TaxAccountCode = "2200", SalesAccountCode = "4000", OriginalInvoiceNumber = "FAC-RB1",
            })).IsSuccess.Should().BeTrue();
            await transaction.RollbackAsync();
        }

        device.Signed.Should().BeEmpty("nothing the caller rolled back reaches the device");
        db.ChangeTracker.Clear();
        (await db.OpenItems.AsNoTracking().AnyAsync(i => i.CompanyId == companyId && (i.DocumentNumber == "FAC-RB1" || i.DocumentNumber == "FAC-RBC1"))).Should().BeFalse();

        // The next invoice that commits is signed, alone: nothing waiting from the rolled-back work runs with it.
        var next = await ar.PostInvoiceAsync(Invoice(companyId, buyer.Id, "FAC-OK1"));
        next.IsSuccess.Should().BeTrue(next.IsFailure ? next.Error.Message : null);
        device.Signed.Should().ContainSingle().Which.Should().Match<FiscalReceiptRequest>(r => r.DocumentNumber == "FAC-OK1" && r.TotalTax == 15m && r.CustomerTaxNumber == "VAT-91");
        (await db.OpenItems.AsNoTracking().SingleAsync(i => i.Id == next.Value.OpenItemId)).FiscalQueued.Should().BeFalse();
    }

    [SkippableFact]
    public async Task A_batch_of_invoices_is_queued_and_signed_by_the_task()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        SimulatedFiscalDeviceProvider device = new();
        await using ServiceProvider provider = _fixture.BuildServiceProvider(s => s.AddSingleton<IFiscalDeviceProvider>(device));
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer ratepayer = new(companyId, "FAC-C3", "Ratepayer", Scenario.Usd) { TaxRegistration = "VAT-33" };
        db.Customers.Add(ratepayer);
        await db.SaveChangesAsync();

        var batch = await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostInvoicesAsync(
            [Invoice(companyId, ratepayer.Id, "FAC-B1"), Invoice(companyId, ratepayer.Id, "FAC-B2"), Invoice(companyId, ratepayer.Id, "FAC-B3")]);
        batch.IsSuccess.Should().BeTrue(batch.IsFailure ? batch.Error.Message : null);
        device.Signed.Should().BeEmpty("a batch is not signed bill by bill while it posts");
        List<long> ids = [.. batch.Value.Select(r => r.OpenItemId)];
        (await db.OpenItems.AsNoTracking().Where(i => ids.Contains(i.Id)).Select(i => i.FiscalQueued).ToListAsync()).Should().AllSatisfy(q => q.Should().BeTrue());

        IScheduledTask task = scope.ServiceProvider.GetServices<IScheduledTask>().Single(t => t.Key == "ar.fiscal-invoices");
        Result<string> run = await task.RunAsync(companyId, DateTimeOffset.UtcNow);
        run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.Message : null);
        device.Signed.Select(r => r.DocumentNumber).Should().Equal("FAC-B1", "FAC-B2", "FAC-B3");
        device.Signed.Should().AllSatisfy(r => (r.Kind, r.TotalGross, r.TotalTax, r.CustomerTaxNumber).Should().Be((FiscalDocumentKind.Invoice, 115m, 15m, "VAT-33")));
        (await db.OpenItems.AsNoTracking().Where(i => ids.Contains(i.Id)).ToListAsync()).Should().AllSatisfy(i => (i.FiscalQueued, i.FiscalCode is null).Should().Be((false, false)));
    }

    private static SalesInvoiceRequest Invoice(long companyId, long customerId, string number) => new()
    {
        CompanyId = companyId, CustomerId = customerId, DocumentNumber = number, DocumentDate = new DateOnly(2026, 6, 2), Currency = Scenario.Usd, NetAmount = 100m, TaxAmount = 15m,
        TaxAccountCode = "2200", TaxCode = "S", SalesAccountCode = "4000", Narrative = "Fiscal after commit",
    };

    // Ends the first save that adds the named open item as PostgreSQL ends one of two deadlocked transactions.
    private sealed class DeadlockOnce(string documentNumber) : SaveChangesInterceptor
    {
        public bool Thrown { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Thrown && eventData.Context is DbContext context
                && context.ChangeTracker.Entries<OpenItem>().Any(e => e.State == EntityState.Added && e.Entity.DocumentNumber == documentNumber))
            {
                Thrown = true;
                throw new Npgsql.PostgresException("deadlock detected", "ERROR", "ERROR", Npgsql.PostgresErrorCodes.DeadlockDetected);
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
