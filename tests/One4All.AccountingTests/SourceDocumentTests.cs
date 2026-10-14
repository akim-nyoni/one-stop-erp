using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers the last step of the drill-down: from a journal to the customer item it raised, or to nothing when the journal is its own source.</summary>
[Collection(AccountingCollection.Name)]
public sealed class SourceDocumentTests
{
    private readonly AccountingFixture _fixture;

    public SourceDocumentTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_journal_leads_to_the_document_it_came_from()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer customer = new(companyId, "SRC-C", "Source Customer", Scenario.Usd);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        TradingPostingResult invoice = (await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "SRC-INV1", DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, NetAmount = 250m, SalesAccountCode = "4000",
        })).Value;

        ISourceDocumentService sources = scope.ServiceProvider.GetRequiredService<ISourceDocumentService>();
        (await sources.DescribeAsync(companyId, invoice.JournalNumber)).Should().Contain("Customer invoice SRC-INV1").And.Contain("SRC-C Source Customer").And.Contain("250.00");
        (await sources.DescribeAsync(companyId, "NO-SUCH")).Should().Contain("not found");
    }
}
