using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Security;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the access check on JSON bodies: a body naming a company outside the user's scope is refused like a query naming it, a body may not
/// contradict the query, and a body's acting or authorising user must be the signed-in user.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ApiBodyAccessTests
{
    private readonly AccountingFixture _fixture;

    public ApiBodyAccessTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Bodies_are_held_to_the_same_company_and_actor_checks()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long home = await Scenario.SeedAsync(db);
        long other = await Scenario.SeedAsync(db);
        string tag = Guid.NewGuid().ToString("N")[..8];
        Role clerk = new($"Body clerk {tag}");
        clerk.Grant("Sales.Invoice", OperationFlags.View | OperationFlags.Create | OperationFlags.Post);
        db.Roles.Add(clerk);
        await db.SaveChangesAsync();
        User user = new($"body.{tag}", "Body clerk");
        user.SetPasswordHash("hash", mustChange: false);
        user.AssignRole(clerk.Id).AddScope(new AccessScope(ScopeDimension.Company, [home.ToString(System.Globalization.CultureInfo.InvariantCulture)]));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        IApiAccessService access = scope.ServiceProvider.GetRequiredService<IApiAccessService>();
        Task<ApiAccessDecision> Post(Dictionary<string, string> query, Dictionary<string, string> body)
            => access.AuthoriseAsync(new ApiAccessRequest(user.Id, "POST", "/api/ar/invoices", query, body));
        string Id(long id) => id.ToString(System.Globalization.CultureInfo.InvariantCulture);

        (await Post([], new() { ["companyId"] = Id(home), ["enteredByUserId"] = Id(user.Id) })).Allowed.Should().BeTrue();
        (await Post([], new() { ["companyId"] = Id(other) })).StatusCode.Should().Be(403);                                   // another company in the body
        (await Post(new() { ["companyId"] = Id(home) }, new() { ["CompanyId"] = Id(other) })).StatusCode.Should().Be(400);  // contradicts the query
        (await Post([], new() { ["companyId"] = Id(home), ["enteredByUserId"] = Id(user.Id + 1000) })).StatusCode.Should().Be(403);
        (await Post([], new() { ["companyId"] = Id(home), ["creditLimitOverrideByUserId"] = Id(user.Id + 1000) })).StatusCode.Should().Be(403);
        (await Post([], new() { ["companyId"] = Id(home), ["narrative"] = "anything" })).Allowed.Should().BeTrue();
    }
}
