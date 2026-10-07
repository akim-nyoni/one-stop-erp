using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Security;
using One4All.Core.Audit;
using One4All.Core.Security;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Security;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers who and where in the trails: with the request's caller and address on the scoped identity (as the API and desktop now set it), a
/// sign-in attempt records the source address, and a change is audited under the caller's name with the address.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class CallerIdentityTests
{
    private readonly AccountingFixture _fixture;

    public CallerIdentityTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Sign_ins_and_changes_carry_the_callers_name_and_address()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        string tag = Guid.NewGuid().ToString("N")[..8];
        IUserAdminService admin = scope.ServiceProvider.GetRequiredService<IUserAdminService>();
        long userId = (await admin.CreateUserAsync($"ci.{tag}", "Caller", null, "Caller2026a")).Value;
        (await admin.ResetPasswordAsync(userId, "Caller2026b")).IsSuccess.Should().BeTrue();

        CurrentUserContext caller = scope.ServiceProvider.GetRequiredService<CurrentUserContext>();
        caller.SetClientAddress("10.1.2.3");
        AuthenticationResult signedIn = await scope.ServiceProvider.GetRequiredService<IAuthenticationService>().AuthenticateAsync($"ci.{tag}", "Caller2026b", "api");
        signedIn.Outcome.Should().Be(AuthenticationOutcome.Success);
        (await db.LoginEvents.AsNoTracking().Where(l => l.UserId == userId).Select(l => l.SourceAddress).ToListAsync()).Should().AllBe("10.1.2.3");

        caller.SignIn(userId, $"ci.{tag}", EffectivePermissions.None, companyId, "api");
        db.Customers.Add(new Customer(companyId, "CI-CUS", "Audited Customer", Scenario.Usd));
        await db.SaveChangesAsync();
        ChangeLogEntry entry = await db.ChangeLog.AsNoTracking().Where(c => c.CompanyId == companyId && c.EntityType == "Customer" && c.Action == AuditAction.Insert)
            .OrderByDescending(c => c.Id).FirstAsync();
        (entry.UserName, entry.UserId, entry.IpAddress, entry.Terminal).Should().Be(($"ci.{tag}", (long?)userId, "10.1.2.3", "api"));
    }
}
