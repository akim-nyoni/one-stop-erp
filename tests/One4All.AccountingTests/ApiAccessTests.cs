using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Security;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers API authorisation: a sales clerk with view-only customers may read the AR area but not change it nor touch the GL, may only act
/// as themselves (a left-out acting user is filled in), and a company scope limits them to that company.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ApiAccessTests
{
    private readonly AccountingFixture _fixture;

    public ApiAccessTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Requests_are_held_to_permissions_company_and_acting_user()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long home = await Scenario.SeedAsync(db);
        long other = await Scenario.SeedAsync(db);
        string tag = Guid.NewGuid().ToString("N")[..8];
        Role clerk = new($"Clerk {tag}");
        clerk.Grant("Sales.Customer", OperationFlags.View);
        clerk.Grant("Sales.Invoice", OperationFlags.View | OperationFlags.Create | OperationFlags.Post);
        Role reader = new($"Reader {tag}");
        reader.Grant("Sales.Customer", OperationFlags.View);
        db.Roles.AddRange(clerk, reader);
        await db.SaveChangesAsync();
        User user = new($"clerk.{tag}", "Clerk");
        user.SetPasswordHash("hash", mustChange: false);
        user.AssignRole(clerk.Id);
        User scoped = new($"scoped.{tag}", "Scoped reader");
        scoped.SetPasswordHash("hash", mustChange: false);
        scoped.AssignRole(reader.Id).AddScope(new AccessScope(ScopeDimension.Company, [home.ToString(System.Globalization.CultureInfo.InvariantCulture)]));
        db.Users.AddRange(user, scoped);
        await db.SaveChangesAsync();

        IApiAccessService access = scope.ServiceProvider.GetRequiredService<IApiAccessService>();
        Task<ApiAccessDecision> Ask(long userId, string method, string path, params (string Key, string Value)[] query)
            => access.AuthoriseAsync(new ApiAccessRequest(userId, method, path, query.ToDictionary(q => q.Key, q => q.Value)));

        (await Ask(user.Id, "GET", "/api/ar/customers/1/statement", ("companyId", $"{home}"))).Allowed.Should().BeTrue();
        (await Ask(user.Id, "POST", "/api/ar/invoices", ("companyId", $"{home}"))).Allowed.Should().BeTrue();
        (await Ask(user.Id, "POST", "/api/gl/journals/submit")).StatusCode.Should().Be(403);
        (await Ask(scoped.Id, "POST", "/api/ar/invoices", ("companyId", $"{home}"))).StatusCode.Should().Be(403);
        (await Ask(user.Id, "GET", "/api/nowhere/at-all")).StatusCode.Should().Be(404);

        // Acting as someone else is refused; leaving it out fills in the signed-in user; an assignee is not the actor.
        (await Ask(user.Id, "POST", "/api/ar/credit-limit-requests/1/approve", ("companyId", $"{home}"), ("userId", $"{user.Id + 1000}"))).StatusCode.Should().Be(403);
        (await Ask(user.Id, "POST", "/api/ar/credit-limit-requests/1/approve", ("companyId", $"{home}"))).MissingActorParameters.Should().Contain("userId");
        (await Ask(user.Id, "GET", "/api/workflow/inbox", ("userId", $"{user.Id}"))).Allowed.Should().BeTrue();

        // The scoped reader sees only their own company.
        (await Ask(scoped.Id, "GET", "/api/ar/customers/1/statement", ("companyId", $"{home}"))).Allowed.Should().BeTrue();
        (await Ask(scoped.Id, "GET", "/api/ar/customers/1/statement", ("companyId", $"{other}"))).StatusCode.Should().Be(403);
    }
}
