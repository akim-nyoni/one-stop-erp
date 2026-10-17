using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Thirteenth audit #10 (brief §3.2 rule 8): a company may queue a module's postings as deferred journals; the sub-ledger updates at once and the batch release posts them.</summary>
[Collection(AccountingCollection.Name)]
public sealed class DeferredPostingModulesTests
{
    private readonly AccountingFixture _fixture;

    public DeferredPostingModulesTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_queued_modules_postings_wait_as_deferred_journals_until_released()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        (await db.Companies.SingleAsync(c => c.Id == companyId)).DeferredPostingModules = "AR, POS";
        Customer customer = new(companyId, "DPM-C", "Queued Customer", Scenario.Usd);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        Result<TradingPostingResult> invoice = await ar.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "DPM-1", DocumentDate = new DateOnly(2026, 3, 10), Currency = Scenario.Usd, NetAmount = 100m, SalesAccountCode = "4000",
        });
        invoice.IsSuccess.Should().BeTrue(invoice.IsFailure ? invoice.Error.ToString() : null);

        // The sub-ledger has the invoice at once; the nominal journal waits as deferred (the period balances do not move until the release).
        (await db.OpenItems.AsNoTracking().AnyAsync(i => i.CompanyId == companyId && i.DocumentNumber == "DPM-1")).Should().BeTrue();
        JournalEntry queued = await db.Journals.AsNoTracking().SingleAsync(j => j.CompanyId == companyId && j.JournalNumber == invoice.Value.JournalNumber);
        queued.Status.Should().Be(JournalStatus.Deferred);

        // A module that is not queued posts straight away.
        (await db.Companies.SingleAsync(c => c.Id == companyId)).DeferredPostingModules = "POS";
        await db.SaveChangesAsync();
        Result<TradingPostingResult> direct = await ar.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "DPM-2", DocumentDate = new DateOnly(2026, 3, 11), Currency = Scenario.Usd, NetAmount = 50m, SalesAccountCode = "4000",
        });
        (await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && j.JournalNumber == direct.Value.JournalNumber).Select(j => j.Status).SingleAsync()).Should().Be(JournalStatus.Posted);

        DeferredReleaseResult released = await scope.ServiceProvider.GetRequiredService<IPostingEngine>().ReleaseAllDeferredAsync(companyId, new DateOnly(2026, 3, 31));
        released.Released.Should().ContainSingle();
        (await db.Journals.AsNoTracking().Where(j => j.Id == queued.Id).Select(j => j.Status).SingleAsync()).Should().Be(JournalStatus.Posted);
    }
}
