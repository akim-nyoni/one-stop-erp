using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the indigent register: a household earning 300 against a 500 threshold is registered for a 50% subsidy until
/// its June review, while one earning 900 is refused; March bills of 300 earn a 150 credit, once only; its review shows
/// up as due in June; after the review date passes, July's bill earns nothing; a review finding 600 revokes it.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class IndigentRegisterTests
{
    private readonly AccountingFixture _fixture;

    public IndigentRegisterTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Registered_households_get_their_subsidy_until_review()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "6950", "Indigent Subsidy", AccountType.Expense));
        Customer poor = new(companyId, "IND-A", "Household A", Scenario.Usd);
        Customer comfortable = new(companyId, "IND-B", "Household B", Scenario.Usd);
        db.Customers.AddRange(poor, comfortable);
        await db.SaveChangesAsync();
        Property home = new(companyId, "101", "Ward 1", poor.Id);
        Property other = new(companyId, "102", "Ward 1", comfortable.Id);
        db.Properties.AddRange(home, other);
        await db.SaveChangesAsync();

        IIndigentRegisterService register = scope.ServiceProvider.GetRequiredService<IIndigentRegisterService>();
        IndigentApplication Apply(Property property, decimal income) => new(property.Id, income, 50m, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), 9);
        long registration = (await register.RegisterAsync(companyId, Apply(home, 300m), 500m)).Value;
        (await register.RegisterAsync(companyId, Apply(other, 900m), 500m)).Error.Code.Should().Be("Indigent.IncomeTooHigh");
        (await register.RegisterAsync(companyId, Apply(home, 300m), 500m)).Error.Code.Should().Be("Indigent.AlreadyRegistered");

        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        async Task Bill(Customer customer, string number, DateOnly date, decimal amount) => (await ar.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = number, DocumentDate = date, Currency = Scenario.Usd, NetAmount = amount, SalesAccountCode = "4000",
        })).IsSuccess.Should().BeTrue();
        await Bill(poor, "MUN-101-03W", new DateOnly(2026, 3, 31), 200m);
        await Bill(poor, "MUN-101-03R", new DateOnly(2026, 3, 31), 100m);
        await Bill(comfortable, "MUN-102-03W", new DateOnly(2026, 3, 31), 400m);

        (await register.ApplySubsidiesAsync(companyId, new DateOnly(2026, 3, 31), "6950")).Value.Should().Equal(new SubsidyLine(home.Id, "101", 300m, 150m));
        (await register.ApplySubsidiesAsync(companyId, new DateOnly(2026, 3, 31), "6950")).Value.Should().BeEmpty();

        (await register.ReviewsDueAsync(companyId, new DateOnly(2026, 6, 20), 30)).Should().Equal(new IndigentReviewDue(registration, home.Id, "101", new DateOnly(2026, 6, 30), false));

        await Bill(poor, "MUN-101-07W", new DateOnly(2026, 7, 31), 100m);
        (await register.ApplySubsidiesAsync(companyId, new DateOnly(2026, 7, 31), "6950")).Value.Should().BeEmpty();
        (await register.ReviewAsync(companyId, registration, 600m, 50m, new DateOnly(2026, 12, 31), 500m, 9)).Value.Should().BeFalse();
        (await db.Set<IndigentRegistration>().AsNoTracking().SingleAsync(r => r.Id == registration)).Status.Should().Be(IndigentStatus.Revoked);

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 7, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Single(r => r.AccountCode == "6950").Debit.Should().Be(150m);
    }
}
