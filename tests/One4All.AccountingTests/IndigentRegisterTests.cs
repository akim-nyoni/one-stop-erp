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
/// Covers the indigent register (audit #29): the qualifying income is the council's policy setting, not a value typed per registration, and a
/// registration keyed by one user is subsidised only once another user holding Municipal.Indigent approve approves it. A household earning 300
/// against the 500 threshold is requested for a 50% subsidy until its June review, while one earning 900 is refused; March bills of 300 USD and
/// 50 EUR earn credits of 150 USD and 25 EUR, each in its own currency, once only, and neither the interest item nor the penalty invoice is
/// subsidised; its review shows up as due in June; after the review date passes, July's bill earns nothing; a review finding 600 revokes it.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class IndigentRegisterTests
{
    // User ids no user created by the test can be given, so the approver is never the clerk by accident.
    private const long Clerk = 900_291;

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
        db.Accounts.Add(new LedgerAccount(companyId, "4810", "Penalty Interest", AccountType.Income));
        db.Add(new FinanceChargePolicy(companyId, 12m, "4810"));
        Customer poor = new(companyId, "IND-A", "Household A", Scenario.Usd);
        Customer comfortable = new(companyId, "IND-B", "Household B", Scenario.Usd);
        Customer refused = new(companyId, "IND-C", "Household C", Scenario.Usd);
        db.Customers.AddRange(poor, comfortable, refused);
        await db.SaveChangesAsync();
        Property home = new(companyId, "101", "Ward 1", poor.Id);
        Property other = new(companyId, "102", "Ward 1", comfortable.Id);
        Property third = new(companyId, "103", "Ward 1", refused.Id);
        db.Properties.AddRange(home, other, third);
        await db.SaveChangesAsync();
        long approver = await Scenario.UserWithAsync(db, "ind-approver", "Municipal.Indigent", Core.Security.OperationFlags.Approve);
        long viewer = await Scenario.UserWithAsync(db, "ind-viewer", "Municipal.Indigent", Core.Security.OperationFlags.View);

        IIndigentRegisterService register = scope.ServiceProvider.GetRequiredService<IIndigentRegisterService>();
        IndigentApplication Apply(Property property, decimal income) => new(property.Id, income, 50m, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), Clerk);

        // No threshold set yet: nothing can be measured against it.
        (await register.RequestAsync(companyId, Apply(home, 300m))).Error.Code.Should().Be("Indigent.NoPolicy");
        (await register.SetPolicyAsync(companyId, 500m)).IsSuccess.Should().BeTrue();
        (await register.PolicyAsync(companyId)).Should().Be(500m);

        long registration = (await register.RequestAsync(companyId, Apply(home, 300m))).Value;
        (await register.RequestAsync(companyId, Apply(other, 900m))).Error.Code.Should().Be("Indigent.IncomeTooHigh");
        (await register.RequestAsync(companyId, Apply(home, 300m))).Error.Code.Should().Be("Indigent.AlreadyRegistered", "a request is already awaiting approval");
        (await register.PendingAsync(companyId)).Select(p => p.RegistrationId).Should().Equal(registration);

        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        async Task Bill(Customer customer, string number, DateOnly date, decimal amount, CurrencyCode? currency = null, string account = "4000") => (await ar.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = number, DocumentDate = date, Currency = currency ?? Scenario.Usd, NetAmount = amount, SalesAccountCode = account,
        })).IsSuccess.Should().BeTrue();
        await Bill(poor, "MUN-101-03W", new DateOnly(2026, 3, 31), 200m);
        await Bill(poor, "MUN-101-03R", new DateOnly(2026, 3, 31), 100m);
        await Bill(poor, "MUN-101-03S", new DateOnly(2026, 3, 31), 50m, Scenario.Eur);
        await Bill(poor, "MUN-101-03P", new DateOnly(2026, 3, 31), 40m, account: "4810");
        await Bill(poor, "MUN-101-03I", new DateOnly(2026, 3, 31), 30m);
        (await db.OpenItems.SingleAsync(i => i.CompanyId == companyId && i.DocumentNumber == "MUN-101-03I")).MarkAsInterestCharge();
        await db.SaveChangesAsync();
        await Bill(comfortable, "MUN-102-03W", new DateOnly(2026, 3, 31), 400m);

        // Pending: not subsidised.
        (await register.ApplySubsidiesAsync(companyId, new DateOnly(2026, 3, 31), "6950")).Value.Should().BeEmpty();

        // The clerk cannot approve their own request, nor can a user without the approve right.
        (await register.ApproveAsync(companyId, registration, Clerk)).Error.Code.Should().Be("Indigent.SelfApproval");
        (await register.ApproveAsync(companyId, registration, viewer)).Error.Code.Should().Be("Indigent.NotAuthorised");
        (await register.ApproveAsync(companyId, registration, approver)).IsSuccess.Should().BeTrue();
        IndigentRegistration approved = await db.Set<IndigentRegistration>().AsNoTracking().SingleAsync(r => r.Id == registration);
        (approved.Status, approved.RequestedByUserId, approved.ApprovedByUserId).Should().Be((IndigentStatus.Active, Clerk, (long?)approver));

        // A request can be turned down, with a reason, and the property can then be requested again.
        long declined = (await register.RequestAsync(companyId, Apply(third, 100m))).Value;
        (await register.RejectAsync(companyId, declined, approver, " ")).Error.Code.Should().Be("Indigent.Reason");
        (await register.RejectAsync(companyId, declined, approver, "Income not proven")).IsSuccess.Should().BeTrue();
        (await db.Set<IndigentRegistration>().AsNoTracking().SingleAsync(r => r.Id == declined)).Status.Should().Be(IndigentStatus.Rejected);
        (await register.ApproveAsync(companyId, declined, approver)).Error.Code.Should().Be("Indigent.NotPending");
        (await register.PendingAsync(companyId)).Should().BeEmpty();

        // Each currency's charges earn the subsidy in that currency; interest and the penalty are left out.
        (await register.ApplySubsidiesAsync(companyId, new DateOnly(2026, 3, 31), "6950")).Value.Should().Equal(
            new SubsidyLine(home.Id, "101", Scenario.Eur, 50m, 25m),
            new SubsidyLine(home.Id, "101", Scenario.Usd, 300m, 150m));
        (await register.ApplySubsidiesAsync(companyId, new DateOnly(2026, 3, 31), "6950")).Value.Should().BeEmpty();
        var credits = await db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.DocumentNumber.StartsWith("IND-101-202603"))
            .OrderBy(i => i.DocumentNumber).Select(i => new { i.DocumentNumber, i.Currency, i.OriginalTransaction }).ToListAsync();
        credits.Select(c => new { c.DocumentNumber, Currency = c.Currency.ToString(), c.OriginalTransaction }).Should().BeEquivalentTo([
            new { DocumentNumber = "IND-101-202603-EUR", Currency = "EUR", OriginalTransaction = 25m },
            new { DocumentNumber = "IND-101-202603-USD", Currency = "USD", OriginalTransaction = 150m }]);

        (await register.ReviewsDueAsync(companyId, new DateOnly(2026, 6, 20), 30)).Should().Equal(new IndigentReviewDue(registration, home.Id, "101", new DateOnly(2026, 6, 30), false));

        await Bill(poor, "MUN-101-07W", new DateOnly(2026, 7, 31), 100m);
        (await register.ApplySubsidiesAsync(companyId, new DateOnly(2026, 7, 31), "6950")).Value.Should().BeEmpty();
        (await register.ReviewAsync(companyId, registration, 600m, 50m, new DateOnly(2026, 12, 31), approver)).Value.Should().BeFalse();
        (await db.Set<IndigentRegistration>().AsNoTracking().SingleAsync(r => r.Id == registration)).Status.Should().Be(IndigentStatus.Revoked);

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 7, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
    }
}
