using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Security;
using One4All.Application.Security;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Security;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers sign-in hardening: authenticator enrolment and the code at sign-in, password history and expiry, the concurrent-session limit
/// (refusing, or ending the oldest) and the idle timeout that closes a session.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AuthenticationHardeningTests
{
    private readonly AccountingFixture _fixture;

    public AuthenticationHardeningTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Two_factor_and_password_history_guard_sign_in()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider(s => s.AddSingleton(new AuthenticationOptions { PasswordHistoryDepth = 2 }));
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IUserAdminService admin = scope.ServiceProvider.GetRequiredService<IUserAdminService>();
        IAuthenticationService auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        string login = $"ah.{Guid.NewGuid().ToString("N")[..8]}";
        long userId = (await admin.CreateUserAsync(login, "Hardened", null, "First2026a")).Value;

        // History: the last two passwords cannot come back.
        (await auth.ChangePasswordAsync(userId, "First2026a", "Second2026b")).Should().BeTrue();
        (await auth.ChangePasswordAsync(userId, "Second2026b", "First2026a")).Should().BeFalse();
        (await auth.ChangePasswordAsync(userId, "Second2026b", "Third2026c")).Should().BeTrue();

        // Two-factor: enrol, confirm with a code, then sign-in needs one.
        (await auth.ConfirmTwoFactorAsync(userId, "123456")).Error.Code.Should().Be("Security.TwoFactorNotStarted");
        (string secret, string uri) = (await auth.BeginTwoFactorAsync(userId)).Value;
        uri.Should().Contain(login);
        (await auth.ConfirmTwoFactorAsync(userId, "000000")).Error.Code.Should().Be("Security.TwoFactorCode");
        (await auth.ConfirmTwoFactorAsync(userId, Totp.Code(secret, DateTimeOffset.UtcNow))).IsSuccess.Should().BeTrue();
        (await auth.AuthenticateAsync(login, "Third2026c")).Outcome.Should().Be(AuthenticationOutcome.TwoFactorRequired);
        (await auth.AuthenticateWithCodeAsync(login, "Third2026c", "000000")).Outcome.Should().Be(AuthenticationOutcome.TwoFactorRequired);
        (await auth.AuthenticateWithCodeAsync(login, "Third2026c", Totp.Code(secret, DateTimeOffset.UtcNow))).Succeeded.Should().BeTrue();
        (await auth.DisableTwoFactorAsync(userId)).IsSuccess.Should().BeTrue();
        (await auth.AuthenticateAsync(login, "Third2026c")).Succeeded.Should().BeTrue();
    }

    [SkippableFact]
    public async Task Sessions_are_limited_timed_out_and_passwords_expire()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        string login = $"as.{Guid.NewGuid().ToString("N")[..8]}";
        await using (ServiceProvider setup = _fixture.BuildServiceProvider())
        {
            await using AsyncServiceScope scope = setup.CreateAsyncScope();
            (await scope.ServiceProvider.GetRequiredService<IUserAdminService>().CreateUserAsync(login, "Sessions", null, "Session2026a")).IsSuccess.Should().BeTrue();
        }

        // One session at a time, refusing the second; ending the first frees the slot.
        await using (ServiceProvider provider = _fixture.BuildServiceProvider(s => s.AddSingleton(new AuthenticationOptions { MaxConcurrentSessions = 1 })))
        {
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            IAuthenticationService auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            AuthenticationResult first = await auth.AuthenticateAsync(login, "Session2026a");
            first.Succeeded.Should().BeTrue();
            (await auth.AuthenticateAsync(login, "Session2026a")).Outcome.Should().Be(AuthenticationOutcome.TooManySessions);
            await auth.EndSessionAsync(first.SessionId!);
            (await auth.AuthenticateAsync(login, "Session2026a")).Succeeded.Should().BeTrue();
        }

        // Or end the oldest; and an idle timeout of nothing closes a session at its next touch.
        await using (ServiceProvider provider = _fixture.BuildServiceProvider(s => s.AddSingleton(new AuthenticationOptions
        {
            MaxConcurrentSessions = 1, EndOldestSessionAtLimit = true, SessionIdleTimeout = TimeSpan.FromHours(1),
        })))
        {
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            IAuthenticationService auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            AuthenticationResult newer = await auth.AuthenticateAsync(login, "Session2026a");
            newer.Succeeded.Should().BeTrue();
            (await auth.TouchSessionAsync(newer.SessionId!)).Should().BeTrue();
            AuthenticationResult newest = await auth.AuthenticateAsync(login, "Session2026a");
            newest.Succeeded.Should().BeTrue();
            (await auth.TouchSessionAsync(newer.SessionId!)).Should().BeFalse();
            (await auth.TouchSessionAsync(newest.SessionId!)).Should().BeTrue();
        }

        await using (ServiceProvider provider = _fixture.BuildServiceProvider(s => s.AddSingleton(new AuthenticationOptions { SessionIdleTimeout = TimeSpan.Zero, PasswordMaxAge = TimeSpan.Zero })))
        {
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            IAuthenticationService auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            AuthenticationResult result = await auth.AuthenticateAsync(login, "Session2026a");
            (result.Succeeded, result.MustChangePassword).Should().Be((true, true));
            await Task.Delay(5);
            (await auth.TouchSessionAsync(result.SessionId!)).Should().BeFalse();
        }
    }
}
