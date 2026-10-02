using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers the printable statement of account: open items, balance and the ageing footer.</summary>
[Collection(AccountingCollection.Name)]
public sealed class StatementDocumentTests
{
    private readonly AccountingFixture _fixture;

    public StatementDocumentTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Customer_statement_renders_open_items_balance_and_ageing()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "STM-C", "Statement Buyer", Scenario.Usd) { PaymentTermsDays = 30 };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;

            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            // Due 2026-02-02 → 118 days overdue at 2026-05-31 (over 90); due 2026-05-15 → 16 days (1-30); due 2026-06-20 → current.
            (await ar.PostInvoiceAsync(Invoice(companyId, customerId, "STM-1", new DateOnly(2026, 1, 3), 400m))).IsSuccess.Should().BeTrue();
            (await ar.PostInvoiceAsync(Invoice(companyId, customerId, "STM-2", new DateOnly(2026, 4, 15), 250m))).IsSuccess.Should().BeTrue();
            (await ar.PostInvoiceAsync(Invoice(companyId, customerId, "STM-3", new DateOnly(2026, 5, 21), 100m))).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IStatementService statements = scope.ServiceProvider.GetRequiredService<IStatementService>();
            var result = await statements.RenderCustomerStatementTextAsync(companyId, customerId, new DateOnly(2026, 5, 31));
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            string text = result.Value;

            text.Should().Contain("STATEMENT OF ACCOUNT");
            text.Should().Contain("Statement Buyer (STM-C)");
            text.Should().Contain("Statement date: 2026-05-31");
            text.Should().Contain("INV STM-1").And.Contain("INV STM-2").And.Contain("INV STM-3");
            text.Should().Contain("BALANCE DUE (USD)").And.Contain("750.00");

            // Ageing footer, one bucket per line.
            string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
            lines.Single(l => l.StartsWith("Current", StringComparison.Ordinal)).Should().EndWith("100.00");
            lines.Single(l => l.StartsWith("1-30 days", StringComparison.Ordinal)).Should().EndWith("250.00");
            lines.Single(l => l.StartsWith("31-60 days", StringComparison.Ordinal)).Should().EndWith("0.00");
            lines.Single(l => l.StartsWith("61-90 days", StringComparison.Ordinal)).Should().EndWith("0.00");
            lines.Single(l => l.StartsWith("Over 90 days", StringComparison.Ordinal)).Should().EndWith("400.00");

            // Every line fits the default width.
            lines.Should().OnlyContain(l => l.Length <= 64);

            (await statements.RenderCustomerStatementTextAsync(companyId, 999_999, new DateOnly(2026, 5, 31))).Error.Code.Should().Be("Ar.CustomerNotFound");
            (await statements.RenderSupplierStatementTextAsync(companyId, 999_999, new DateOnly(2026, 5, 31))).Error.Code.Should().Be("Ap.SupplierNotFound");
        }
    }

    private static SalesInvoiceRequest Invoice(long companyId, long customerId, string number, DateOnly date, decimal net) => new()
    {
        CompanyId = companyId, CustomerId = customerId, DocumentNumber = number, DocumentDate = date,
        Currency = Scenario.Usd, NetAmount = net, TaxAmount = 0m, SalesAccountCode = "4000",
    };
}
