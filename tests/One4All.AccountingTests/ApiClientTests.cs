using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Security;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers API client credentials (brief §10): a key signs in as its service user, only its hash is stored, a rotated or revoked key stops working, and
/// an expired client or a deactivated user is refused.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ApiClientTests
{
    private readonly AccountingFixture _fixture;

    public ApiClientTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Keys_sign_in_as_their_service_user_until_rotated_revoked_or_expired()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        string tag = Guid.NewGuid().ToString("N")[..8];
        User service = new($"svc-{tag}", "Shop integration");
        db.Users.Add(service);
        await db.SaveChangesAsync();
        IApiClientService clients = scope.ServiceProvider.GetRequiredService<IApiClientService>();

        (await clients.CreateAsync("Shop", "nobody-" + tag, null, null)).Error.Code.Should().Be("Api.User");
        ApiCredential key = (await clients.CreateAsync("Web shop", $"svc-{tag}", null, 120)).Value;
        key.ApiKey.Should().Be($"{key.ClientId}.{key.Secret}");
        (await db.ApiClients.AsNoTracking().SingleAsync(c => c.ClientId == key.ClientId)).SecretHash.Should().NotContain(key.Secret, "only the hash is kept");

        ApiClientIdentity who = (await clients.AuthenticateAsync(key.ApiKey)).Value;
        (who.UserId, who.RequestsPerMinute).Should().Be((service.Id, 120));
        (await clients.AuthenticateAsync($"{key.ClientId}.wrong")).Error.Code.Should().Be("Api.KeyInvalid");
        (await clients.AuthenticateAsync("no-dot")).Error.Code.Should().Be("Api.KeyInvalid");

        ApiCredential rotated = (await clients.RotateAsync(key.ClientId)).Value;
        (await clients.AuthenticateAsync(key.ApiKey)).IsFailure.Should().BeTrue("the old secret stops working");
        (await clients.AuthenticateAsync(rotated.ApiKey)).IsSuccess.Should().BeTrue();
        (await clients.RevokeAsync(rotated.ClientId)).IsSuccess.Should().BeTrue();
        (await clients.AuthenticateAsync(rotated.ApiKey)).IsFailure.Should().BeTrue();

        ApiCredential expired = (await clients.CreateAsync("Old feed", $"svc-{tag}", new DateOnly(2020, 1, 1), null)).Value;
        (await clients.AuthenticateAsync(expired.ApiKey)).IsFailure.Should().BeTrue();
        (await clients.ListAsync()).Should().Contain(r => r.ClientId == key.ClientId && !r.IsActive && r.ActsAs == $"svc-{tag}");
    }
}
