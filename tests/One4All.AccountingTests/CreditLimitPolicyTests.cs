using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers the credit-limit check policy per customer: block refuses, warn posts flagged and logged, ignore does not check.</summary>
[Collection(AccountingCollection.Name)]
public sealed class CreditLimitPolicyTests
{
    private readonly AccountingFixture _fixture;

    public CreditLimitPolicyTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Each_customer_has_its_own_credit_limit_policy()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer blocked = new(companyId, "CLP-B", "Blocked", Scenario.Usd) { CreditLimit = 100m };
        Customer warned = new(companyId, "CLP-W", "Warned", Scenario.Usd) { CreditLimit = 100m, CreditLimitPolicy = CreditLimitPolicy.Warn };
        Customer free = new(companyId, "CLP-I", "Ignored", Scenario.Usd) { CreditLimit = 100m, CreditLimitPolicy = CreditLimitPolicy.Ignore };
        db.Customers.AddRange(blocked, warned, free);
        await db.SaveChangesAsync();

        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        SalesInvoiceRequest Invoice(Customer c, string number) => new()
        {
            CompanyId = companyId, CustomerId = c.Id, DocumentNumber = number, DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, NetAmount = 150m, SalesAccountCode = "4000",
        };

        (await ar.PostInvoiceAsync(Invoice(blocked, "CLP-1"))).Error.Code.Should().Be("Ar.CreditLimitExceeded");
        TradingPostingResult warn = (await ar.PostInvoiceAsync(Invoice(warned, "CLP-2"))).Value;
        warn.OverCreditLimit.Should().BeTrue();
        TradingPostingResult ignored = (await ar.PostInvoiceAsync(Invoice(free, "CLP-3"))).Value;
        ignored.OverCreditLimit.Should().BeFalse();

        List<CreditLimitOverride> logged = await db.Set<CreditLimitOverride>().AsNoTracking().Where(o => o.CompanyId == companyId).ToListAsync();
        logged.Should().ContainSingle().Which.Should().Match<CreditLimitOverride>(o => o.CustomerId == warned.Id && o.IsWarning && o.BalanceAfter == 150m);
    }
}
