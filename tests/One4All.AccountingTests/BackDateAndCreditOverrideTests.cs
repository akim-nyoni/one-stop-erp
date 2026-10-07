using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Security;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the back-date and credit-limit override permissions: a person posting further back than the company allows is refused unless
/// they hold the back-date permission (postings by no named person, and dates within the allowance, are unaffected); an invoice over a
/// customer's credit limit goes through only with a supervisor holding the override (not the person raising it, not a user without it),
/// and each override is kept.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BackDateAndCreditOverrideTests
{
    private readonly AccountingFixture _fixture;

    public BackDateAndCreditOverrideTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Back_dating_and_credit_overrides_need_their_permissions()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        DateOnly old = new(2026, 1, 15), today = DateOnly.FromDateTime(DateTime.UtcNow);
        (await db.Companies.SingleAsync(c => c.Id == companyId)).BackDateAllowanceDays = today.DayNumber - old.DayNumber - 1;
        Role backDaters = new($"Back-daters {companyId}"), creditControl = new($"Credit control {companyId}");
        backDaters.Grant("GeneralLedger.Journal", OperationFlags.View | OperationFlags.Create | OperationFlags.BackDate);
        creditControl.Grant("Sales.Customer", OperationFlags.View | OperationFlags.OverrideCreditLimit);
        db.Roles.AddRange(backDaters, creditControl);
        User clerk = new($"bd-clerk-{companyId}", "Clerk"), senior = new($"bd-senior-{companyId}", "Senior"), manager = new($"bd-mgr-{companyId}", "Credit Manager");
        db.Users.AddRange(clerk, senior, manager);
        Customer customer = new(companyId, "BD-CUS", "Stretched Customer", Scenario.Usd) { CreditLimit = 100m };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        senior.AssignRole(backDaters.Id);
        manager.AssignRole(creditControl.Id);
        await db.SaveChangesAsync();

        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        JournalRequest Journal(string number, long? by) => new()
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = old, TransactionCurrency = Scenario.Usd, DocumentType = "Manual", DocumentNumber = number, Narrative = "Late accrual",
            PostedByUserId = by, Lines =
            [
                new JournalLineRequest { AccountCode = "6100", Side = JournalSide.Debit, TransactionAmount = 10m },
                new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = 10m },
            ],
        };
        (await engine.PostAsync(Journal("BD-J1", clerk.Id))).Error.Code.Should().Be("Posting.BackDateNotAllowed");
        (await engine.PostAsync(Journal("BD-J2", senior.Id))).IsSuccess.Should().BeTrue();
        (await engine.PostAsync(Journal("BD-J3", null))).IsSuccess.Should().BeTrue("a system posting names no person");
        (await db.Companies.SingleAsync(c => c.Id == companyId)).BackDateAllowanceDays += 10;
        await db.SaveChangesAsync();
        (await engine.PostAsync(Journal("BD-J4", clerk.Id))).IsSuccess.Should().BeTrue("within the allowance");

        // Credit limit 100: an invoice of 150 needs a manager's override.
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        SalesInvoiceRequest Invoice(string number, long? overrideBy) => new()
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = number, DocumentDate = today, Currency = Scenario.Usd, NetAmount = 150m, SalesAccountCode = "4000",
            EnteredByUserId = clerk.Id, CreditLimitOverrideByUserId = overrideBy,
        };
        (await ar.PostInvoiceAsync(Invoice("BD-I1", null))).Error.Code.Should().Be("Ar.CreditLimitExceeded");
        (await ar.PostInvoiceAsync(Invoice("BD-I1", clerk.Id))).Error.Code.Should().Be("Ar.CreditOverrideSelf");
        (await ar.PostInvoiceAsync(Invoice("BD-I1", senior.Id))).Error.Code.Should().Be("Ar.CreditOverrideNotAllowed");
        (await ar.PostInvoiceAsync(Invoice("BD-I1", manager.Id))).IsSuccess.Should().BeTrue();
        CreditLimitOverride kept = await db.Set<CreditLimitOverride>().AsNoTracking().SingleAsync(o => o.CompanyId == companyId);
        (kept.DocumentNumber, kept.CreditLimit, kept.BalanceAfter, kept.AuthorisedByUserId, kept.EnteredByUserId).Should().Be(("BD-I1", 100m, 150m, manager.Id, (long?)clerk.Id));
    }
}
