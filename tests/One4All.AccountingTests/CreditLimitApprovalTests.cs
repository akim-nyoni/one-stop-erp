using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the credit limit approval workflow: with a 1,000 limit a 1,200 invoice is refused; lowering the limit to 800
/// takes effect at once; raising it to 2,000 is requested, cannot be approved by the requester or requested twice, and
/// takes effect only on a second user's approval, after which the invoice posts; a rejected request to remove the limit
/// changes nothing.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class CreditLimitApprovalTests
{
    private const long Clerk = 41;
    private const long Manager = 42;

    private readonly AccountingFixture _fixture;

    public CreditLimitApprovalTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Credit_limits_rise_only_with_a_second_users_approval()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer customer = new(companyId, "CL-C", "Growing Buyer", Scenario.Usd) { CreditLimit = 1_000m };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        SalesInvoiceRequest big = new()
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "CL-I1", DocumentDate = new DateOnly(2026, 3, 10), Currency = Scenario.Usd, NetAmount = 1_200m, SalesAccountCode = "4000",
        };
        (await ar.PostInvoiceAsync(big)).Error.Code.Should().Be("Ar.CreditLimitExceeded");

        ICreditLimitService limits = scope.ServiceProvider.GetRequiredService<ICreditLimitService>();
        (await limits.LowerAsync(companyId, customer.Id, 1_500m)).Error.Code.Should().Be("Ar.NotALowering");
        (await limits.LowerAsync(companyId, customer.Id, 800m)).IsSuccess.Should().BeTrue();
        (await db.Customers.AsNoTracking().SingleAsync(c => c.Id == customer.Id)).CreditLimit.Should().Be(800m);

        (await limits.RequestIncreaseAsync(companyId, customer.Id, 500m, "Smaller", Clerk)).Error.Code.Should().Be("Ar.NotAnIncrease");
        long request = (await limits.RequestIncreaseAsync(companyId, customer.Id, 2_000m, "New three-year contract", Clerk)).Value;
        (await limits.RequestIncreaseAsync(companyId, customer.Id, 3_000m, "Again", Clerk)).Error.Code.Should().Be("Ar.LimitRequestPending");
        (await limits.PendingAsync(companyId)).Should().ContainSingle().Which.Should().BeEquivalentTo(new { CurrentLimit = 800m, RequestedLimit = 2_000m, RequestedByUserId = Clerk });
        (await ar.PostInvoiceAsync(big)).Error.Code.Should().Be("Ar.CreditLimitExceeded");

        (await limits.ApproveAsync(companyId, request, Clerk)).Error.Code.Should().Be("Ar.SelfApproval");
        (await limits.ApproveAsync(companyId, request, Manager)).IsSuccess.Should().BeTrue();
        (await ar.PostInvoiceAsync(big)).IsSuccess.Should().BeTrue();

        long unlimited = (await limits.RequestIncreaseAsync(companyId, customer.Id, 0m, "Remove the limit", Clerk)).Value;
        (await limits.RejectAsync(companyId, unlimited, Manager, "Not without a guarantee")).IsSuccess.Should().BeTrue();
        (await db.Customers.AsNoTracking().SingleAsync(c => c.Id == customer.Id)).CreditLimit.Should().Be(2_000m);
        (await limits.PendingAsync(companyId)).Should().BeEmpty();
    }
}
