using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Accounting;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// A dollar company charges its euro subsidiary 100 euros: each side books it in its own base currency (110 dollars at 1.10, 100 euros); a user
/// allowed into the parent only cannot post into the subsidiary's books.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class IntercompanyCurrencyAndAccessTests
{
    private readonly AccountingFixture _fixture;

    public IntercompanyCurrencyAndAccessTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Intercompany_charges_cross_currencies_and_need_access_to_both_companies()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long parent = await Scenario.SeedAsync(db);
        long subsidiary = await Scenario.SeedAsync(db, baseCurrency: Scenario.Eur);
        db.Accounts.Add(new LedgerAccount(parent, "1700", "Due from Subsidiary", AccountType.Asset));
        db.Accounts.Add(new LedgerAccount(subsidiary, "2700", "Due to Parent", AccountType.Liability));
        Role clerk = new($"Interco clerk {parent}");
        clerk.Grant("Sales.Customer", OperationFlags.View);
        db.Roles.Add(clerk);
        await db.SaveChangesAsync();
        User local = new($"ic-local-{parent}", "Parent-only clerk");
        local.SetPasswordHash("hash", mustChange: false);
        local.AssignRole(clerk.Id).AddScope(new AccessScope(ScopeDimension.Company, [parent.ToString(System.Globalization.CultureInfo.InvariantCulture)]));
        db.Users.Add(local);
        await db.SaveChangesAsync();

        IIntercompanyService interco = scope.ServiceProvider.GetRequiredService<IIntercompanyService>();
        IntercompanyJournalRequest Charge(long userId) => new()
        {
            FromCompanyId = parent, ToCompanyId = subsidiary, Amount = 100m, Currency = Scenario.Eur, Date = new DateOnly(2026, 3, 10), DocumentNumber = $"ICX-{userId}",
            FromAccountCode = "4000", FromIntercompanyAccountCode = "1700", ToAccountCode = "6000", ToIntercompanyAccountCode = "2700", PostedByUserId = userId,
        };
        (await interco.PostAsync(Charge(local.Id))).Error.Code.Should().Be("Interco.NoAccess");
        var posted = await interco.PostAsync(Charge(0));
        posted.IsSuccess.Should().BeTrue(posted.IsFailure ? posted.Error.ToString() : null);

        decimal Base(long company, string account) => db.Journals.AsNoTracking().Where(j => j.CompanyId == company && j.DocumentNumber == "ICX-0").SelectMany(j => j.Lines)
            .Where(l => l.AccountCode == account).Sum(l => l.BaseDebit - l.BaseCredit);
        (Base(parent, "1700"), Base(subsidiary, "2700")).Should().Be((110m, -100m));
    }
}
