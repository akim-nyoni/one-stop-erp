using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the credit-limit check under concurrency: a customer with a 1,000 limit is sent eight 200 invoices at once, from
/// separate connections. Five fit, so exactly five post and three are refused for the limit; none fails for contention.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class CreditLimitConcurrencyTests
{
    private const int Invoices = 8;

    private readonly AccountingFixture _fixture;

    public CreditLimitConcurrencyTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Concurrent_invoices_cannot_together_breach_the_credit_limit()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "RACE-C", "Racing Buyer", Scenario.Usd) { CreditLimit = 1_000m };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;
        }

        using Barrier start = new(Invoices);
        Result<TradingPostingResult>[] results = await Task.WhenAll(Enumerable.Range(1, Invoices).Select(i => Task.Run(async () =>
        {
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            start.SignalAndWait();
            return await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = customerId, DocumentNumber = $"RACE-{i}", DocumentDate = new DateOnly(2026, 3, 10),
                Currency = Scenario.Usd, NetAmount = 200m, SalesAccountCode = "4000",
            });
        })));

        results.Count(r => r.IsSuccess).Should().Be(5);
        results.Where(r => r.IsFailure).Should().OnlyContain(r => r.Error.Code == "Ar.CreditLimitExceeded");
    }
}
