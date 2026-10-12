using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers balance-forward statements: a February invoice of 100 is brought forward into March, where a receipt of 40 and an invoice of 50
/// run the balance to 110; a customer set to balance-forward gets that style by default.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BalanceForwardStatementTests
{
    private readonly AccountingFixture _fixture;

    public BalanceForwardStatementTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task The_period_opens_with_the_balance_brought_forward_and_runs_to_the_balance_carried_forward()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer customer = new(companyId, "BF-CUS", "Forward customer", Scenario.Usd);
        db.Add(customer);
        await db.SaveChangesAsync();

        ISalesLedgerService sales = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        SalesInvoiceRequest Invoice(string number, DateOnly date, decimal net) => new()
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = number, DocumentDate = date, Currency = Scenario.Usd, NetAmount = net, SalesAccountCode = "4000",
        };
        (await sales.PostInvoiceAsync(Invoice("BF-1", new DateOnly(2026, 2, 10), 100m))).IsSuccess.Should().BeTrue();
        (await sales.PostReceiptAsync(new ReceiptRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "BF-R1", ReceiptDate = new DateOnly(2026, 3, 5), Currency = Scenario.Usd, Amount = 40m, BankAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();
        (await sales.PostInvoiceAsync(Invoice("BF-2", new DateOnly(2026, 3, 20), 50m))).IsSuccess.Should().BeTrue();

        IStatementService statements = scope.ServiceProvider.GetRequiredService<IStatementService>();
        string text = (await statements.RenderCustomerBalanceForwardTextAsync(companyId, customer.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31))).Value;
        text.Should().Contain("Balance brought forward").And.Contain("100.00").And.Contain("RCT BF-R1").And.Contain("60.00").And.Contain("INV BF-2")
            .And.NotContain("INV BF-1").And.Contain("BALANCE CARRIED FORWARD").And.Contain("110.00");
        (await statements.RenderCustomerBalanceForwardTextAsync(companyId, customer.Id, new DateOnly(2026, 4, 1), new DateOnly(2026, 3, 1))).Error.Code.Should().Be("Ar.StatementPeriod");

        (await statements.RenderCustomerStatementTextAsync(companyId, customer.Id, new DateOnly(2026, 3, 31))).Value.Should().NotContain("brought forward");
        customer.StatementType = StatementType.BalanceForward;
        await db.SaveChangesAsync();
        (await statements.RenderCustomerStatementTextAsync(companyId, customer.Id, new DateOnly(2026, 3, 31))).Value.Should().Contain("Balance brought forward");
    }
}
