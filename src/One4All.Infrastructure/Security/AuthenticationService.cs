using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Security;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using One4All.Licensing.Core;

namespace One4All.Infrastructure.Security;

/// <summary>Tunable authentication policy (brief §5.2).</summary>
public sealed class AuthenticationOptions
{
    /// <summary>Consecutive failed attempts allowed before lockout.</summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>How long an account stays locked after too many failures.</summary>
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);
}

/// <summary>
/// Authenticates users by password or PIN with Argon2id verification and account lockout, and resolves
/// their permissions on success. Failed attempts and lockouts are persisted on the user aggregate.
/// </summary>
public sealed class AuthenticationService : IAuthenticationService
{
    private readonly One4AllDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IPermissionService _permissions;
    private readonly IClock _clock;
    private readonly AuthenticationOptions _options;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="hasher">The password hasher.</param>
    /// <param name="permissions">The permission service.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="options">Authentication policy.</param>
    public AuthenticationService(
        One4AllDbContext db,
        IPasswordHasher hasher,
        IPermissionService permissions,
        IClock clock,
        AuthenticationOptions? options = null)
    {
        _db = db;
        _hasher = hasher;
        _permissions = permissions;
        _clock = clock;
        _options = options ?? new AuthenticationOptions();
    }

    /// <inheritdoc />
    public Task<AuthenticationResult> AuthenticateAsync(string username, string password, string? terminal = null, CancellationToken ct = default) =>
        AuthenticateCoreAsync(username, password, usePin: false, ct);

    /// <inheritdoc />
    public Task<AuthenticationResult> AuthenticateByPinAsync(string username, string pin, string? terminal = null, CancellationToken ct = default) =>
        AuthenticateCoreAsync(username, pin, usePin: true, ct);

    private async Task<AuthenticationResult> AuthenticateCoreAsync(string username, string secret, bool usePin, CancellationToken ct)
    {
        string normalized = username.Trim().ToUpperInvariant();
        User? user = await _db.Users
            .FirstOrDefaultAsync(u => u.NormalizedUsername == normalized, ct)
            .ConfigureAwait(false);

        if (user is null)
        {
            return AuthenticationResult.Failure(AuthenticationOutcome.InvalidCredentials, "Invalid username or password.");
        }

        if (!user.IsActive)
        {
            return AuthenticationResult.Failure(AuthenticationOutcome.Inactive, "This account is inactive.");
        }

        DateTimeOffset now = _clock.UtcNow;
        if (user.IsLockedOut(now))
        {
            return AuthenticationResult.Failure(AuthenticationOutcome.LockedOut,
                $"Account locked. Try again after {user.LockoutEndUtc:u}.");
        }

        string? storedHash = usePin ? user.PinHash : user.PasswordHash;
        bool ok = storedHash is not null && _hasher.Verify(storedHash, secret);

        if (!ok)
        {
            user.RegisterFailedLogin(now, _options.MaxFailedAttempts, _options.LockoutDuration);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            return user.IsLockedOut(now)
                ? AuthenticationResult.Failure(AuthenticationOutcome.LockedOut, "Too many attempts; account locked.")
                : AuthenticationResult.Failure(AuthenticationOutcome.InvalidCredentials, "Invalid username or password.");
        }

        user.RegisterSuccessfulLogin(now);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        EffectivePermissions permissions = await _permissions.ResolveAsync(user.Id, ct).ConfigureAwait(false);

        return AuthenticationResult.Success(
            user.Id, user.Username, user.DisplayName, user.IsSystemAdministrator, user.MustChangePassword, permissions);
    }

    /// <inheritdoc />
    public async Task<bool> ChangePasswordAsync(long userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        User? user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user?.PasswordHash is null || !_hasher.Verify(user.PasswordHash, currentPassword))
        {
            return false;
        }

        user.SetPasswordHash(_hasher.Hash(newPassword), mustChange: false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }
}
