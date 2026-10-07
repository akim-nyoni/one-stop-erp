using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Security;
using One4All.Core.Common;
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

    /// <summary>Gets or sets how many earlier passwords may not be reused (0: none remembered).</summary>
    public int PasswordHistoryDepth { get; set; } = 5;

    /// <summary>Gets or sets how long a password lasts before it must be changed (null: it never expires).</summary>
    public TimeSpan? PasswordMaxAge { get; set; }

    /// <summary>Gets or sets how long a session may sit idle before it locks (null: no timeout).</summary>
    public TimeSpan? SessionIdleTimeout { get; set; }

    /// <summary>Gets or sets how many sessions a user may have open at once (0: no limit).</summary>
    public int MaxConcurrentSessions { get; set; }

    /// <summary>Gets or sets a value indicating whether, at the session limit, the oldest session is ended (true) or the new sign-in refused.</summary>
    public bool EndOldestSessionAtLimit { get; set; }

    /// <summary>Gets or sets the issuer shown in authenticator apps.</summary>
    public string TwoFactorIssuer { get; set; } = "One4All";
}

/// <summary>
/// Authenticates users by password or PIN with Argon2id verification and account lockout, and resolves
/// their permissions on success. Failed attempts and lockouts are persisted on the user aggregate.
/// </summary>
public sealed class AuthenticationService : IAuthenticationService
{
    private readonly Application.Abstractions.Licensing.ILicenceService? _licence;
    private readonly ICurrentUser? _currentUser;
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
        AuthenticationOptions? options = null,
        Application.Abstractions.Licensing.ILicenceService? licence = null,
        ICurrentUser? currentUser = null)
    {
        _currentUser = currentUser;
        _licence = licence;
        _db = db;
        _hasher = hasher;
        _permissions = permissions;
        _clock = clock;
        _options = options ?? new AuthenticationOptions();
    }

    /// <inheritdoc />
    public Task<AuthenticationResult> AuthenticateAsync(string username, string password, string? terminal = null, CancellationToken ct = default) =>
        AuthenticateCoreAsync(username, password, usePin: false, terminal, ct);

    /// <inheritdoc />
    public Task<AuthenticationResult> AuthenticateWithCodeAsync(string username, string password, string? oneTimeCode, string? terminal = null, CancellationToken ct = default) =>
        AuthenticateCoreAsync(username, password, usePin: false, terminal, ct, oneTimeCode);

    /// <inheritdoc />
    public Task<AuthenticationResult> AuthenticateByPinAsync(string username, string pin, string? terminal = null, CancellationToken ct = default) =>
        AuthenticateCoreAsync(username, pin, usePin: true, terminal, ct);

    // Every attempt is logged (SYS236 / SYS249); a success opens a session.
    private async Task<AuthenticationResult> AuthenticateCoreAsync(string username, string secret, bool usePin, string? terminal, CancellationToken ct, string? oneTimeCode = null)
    {
        string normalized = username.Trim().ToUpperInvariant();
        User? user = await _db.Users
            .FirstOrDefaultAsync(u => u.NormalizedUsername == normalized, ct)
            .ConfigureAwait(false);
        DateTimeOffset now = _clock.UtcNow;
        string method = usePin ? "PIN" : "Password";
        async Task<AuthenticationResult> LogAsync(AuthenticationResult result, string? sessionId = null)
        {
            _db.LoginEvents.Add(new LoginEvent(user?.Id, username.Trim(), now, terminal, method, result.Outcome.ToString(), sessionId) { SourceAddress = _currentUser?.ClientAddress });
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return result;
        }

        if (user is null)
        {
            return await LogAsync(AuthenticationResult.Failure(AuthenticationOutcome.InvalidCredentials, "Invalid username or password.")).ConfigureAwait(false);
        }

        if (!user.IsActive)
        {
            return await LogAsync(AuthenticationResult.Failure(AuthenticationOutcome.Inactive, "This account is inactive.")).ConfigureAwait(false);
        }

        // The licence's named users are the earliest active accounts; later ones cannot sign in past the limit (brief §11.5).
        if (_licence is { Enforced: true })
        {
            int place = await _db.Users.CountAsync(u => u.IsActive && u.Id <= user.Id, ct).ConfigureAwait(false);
            Core.Common.Result named = _licence.WithinLimit("named_users", place);
            if (named.IsFailure)
            {
                return await LogAsync(AuthenticationResult.Failure(AuthenticationOutcome.NotLicensed, named.Error.Message)).ConfigureAwait(false);
            }
        }

        if (user.IsLockedOut(now))
        {
            return await LogAsync(AuthenticationResult.Failure(AuthenticationOutcome.LockedOut,
                $"Account locked. Try again after {user.LockoutEndUtc:u}.")).ConfigureAwait(false);
        }

        string? storedHash = usePin ? user.PinHash : user.PasswordHash;
        bool ok = storedHash is not null && _hasher.Verify(storedHash, secret);

        if (!ok)
        {
            user.RegisterFailedLogin(now, _options.MaxFailedAttempts, _options.LockoutDuration);
            return await LogAsync(user.IsLockedOut(now)
                ? AuthenticationResult.Failure(AuthenticationOutcome.LockedOut, "Too many attempts; account locked.")
                : AuthenticationResult.Failure(AuthenticationOutcome.InvalidCredentials, "Invalid username or password.")).ConfigureAwait(false);
        }

        // Back-office sign-in with two-factor needs the authenticator code too (the till's PIN sign-in does not).
        if (!usePin && user.TwoFactorEnabled && user.TwoFactorSecret is string totpSecret)
        {
            if (string.IsNullOrWhiteSpace(oneTimeCode))
            {
                return await LogAsync(AuthenticationResult.Failure(AuthenticationOutcome.TwoFactorRequired, "Enter the code from your authenticator app.")).ConfigureAwait(false);
            }

            if (!Application.Security.Totp.Verify(totpSecret, oneTimeCode, now))
            {
                user.RegisterFailedLogin(now, _options.MaxFailedAttempts, _options.LockoutDuration);
                return await LogAsync(AuthenticationResult.Failure(AuthenticationOutcome.TwoFactorRequired, "The authenticator code is not right.")).ConfigureAwait(false);
            }
        }

        // The concurrent-session policy: refuse at the limit, or end the oldest open session.
        if (_options.MaxConcurrentSessions > 0)
        {
            List<LoginEvent> open = await OpenSessionsAsync(user.Id, now, ct).ConfigureAwait(false);
            if (open.Count >= _options.MaxConcurrentSessions)
            {
                if (!_options.EndOldestSessionAtLimit)
                {
                    return await LogAsync(AuthenticationResult.Failure(AuthenticationOutcome.TooManySessions,
                        $"You already have {open.Count} session(s) open; sign out of one first.")).ConfigureAwait(false);
                }

                open.OrderBy(s => s.OccurredOnUtc).Take(open.Count - _options.MaxConcurrentSessions + 1).ToList().ForEach(s => s.End(now));
            }
        }

        // The licence's concurrent-user limit: everyone else signed in now, plus this user.
        if (_licence is { Enforced: true } && _licence.GetLimit("concurrent_users") is int concurrentLimit)
        {
            List<LoginEvent> everyone = await _db.LoginEvents.Where(l => l.SessionId != null && l.EndedOnUtc == null && l.UserId != user.Id).ToListAsync(ct).ConfigureAwait(false);
            int others = (_options.SessionIdleTimeout is TimeSpan idleFor ? everyone.Where(l => (l.LastSeenUtc ?? l.OccurredOnUtc) + idleFor >= now) : everyone)
                .Select(l => l.UserId).Distinct().Count();
            if (others + 1 > concurrentLimit)
            {
                return await LogAsync(AuthenticationResult.Failure(AuthenticationOutcome.TooManySessions,
                    $"The licence allows {concurrentLimit} user(s) signed in at once and that many are; try again when someone signs out.")).ConfigureAwait(false);
            }
        }

        user.RegisterSuccessfulLogin(now);
        bool expired = !usePin && _options.PasswordMaxAge is TimeSpan maxAge && (user.PasswordChangedUtc ?? DateTimeOffset.MinValue) + maxAge < now;
        EffectivePermissions permissions = await _permissions.ResolveAsync(user.Id, ct).ConfigureAwait(false);
        string session = Guid.NewGuid().ToString("N");
        _db.ChangeLog.Add(await LoginEntryAsync(user.Id, user.Username, Core.Audit.AuditAction.Login, terminal, session, method, ct).ConfigureAwait(false));
        return await LogAsync(AuthenticationResult.Success(
            user.Id, user.Username, user.DisplayName, user.IsSystemAdministrator, user.MustChangePassword || expired, permissions, session), session).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task EndSessionAsync(string sessionId, CancellationToken ct = default)
    {
        LoginEvent? login = await _db.LoginEvents.FirstOrDefaultAsync(l => l.SessionId == sessionId, ct).ConfigureAwait(false);
        if (login is not null && login.EndedOnUtc is null)
        {
            login.End(_clock.UtcNow);
            _db.ChangeLog.Add(await LoginEntryAsync(login.UserId, login.Username, Core.Audit.AuditAction.Logout, login.Terminal, sessionId, login.Method, ct).ConfigureAwait(false));
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<bool> ChangePasswordAsync(long userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        User? user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user?.PasswordHash is null || !_hasher.Verify(user.PasswordHash, currentPassword)
            || Application.Security.PasswordPolicy.Problem(newPassword, user.Username) is not null || _hasher.Verify(user.PasswordHash, newPassword))
        {
            return false;
        }

        // Not one of the recent passwords either.
        List<string> recent = await _db.PasswordHistories.Where(h => h.UserId == userId).OrderByDescending(h => h.SetOnUtc).Take(_options.PasswordHistoryDepth)
            .Select(h => h.PasswordHash).ToListAsync(ct).ConfigureAwait(false);
        if (recent.Any(h => _hasher.Verify(h, newPassword)))
        {
            return false;
        }

        DateTimeOffset now = _clock.UtcNow;
        _db.PasswordHistories.Add(new PasswordHistory(userId, user.PasswordHash, now));
        user.SetPasswordHash(_hasher.Hash(newPassword), mustChange: false);
        user.StampPasswordChange(now);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> TouchSessionAsync(string sessionId, CancellationToken ct = default)
    {
        LoginEvent? login = await _db.LoginEvents.FirstOrDefaultAsync(l => l.SessionId == sessionId, ct).ConfigureAwait(false);
        if (login is null || login.EndedOnUtc is not null)
        {
            return false;
        }

        DateTimeOffset now = _clock.UtcNow;
        if (_options.SessionIdleTimeout is TimeSpan idle && (login.LastSeenUtc ?? login.OccurredOnUtc) + idle < now)
        {
            login.End(now);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return false;
        }

        login.Touch(now);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task<Result> ReauthenticateAsync(string sessionId, string password, string? oneTimeCode, CancellationToken ct = default)
    {
        LoginEvent? login = await _db.LoginEvents.FirstOrDefaultAsync(l => l.SessionId == sessionId, ct).ConfigureAwait(false);
        User? user = login?.UserId is long userId ? await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false) : null;
        DateTimeOffset now = _clock.UtcNow;
        if (login is null || login.EndedOnUtc is not null || user is null || !user.IsActive)
        {
            return Result.Failure(Error.Rule("Security.SessionEnded", "This session has ended; sign in again."));
        }

        if (user.IsLockedOut(now))
        {
            login.End(now);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Failure(Error.Rule("Security.SessionEnded", "The account is locked; sign in again later."));
        }

        bool passwordOk = user.PasswordHash is string hash && _hasher.Verify(hash, password ?? string.Empty);
        bool codeOk = !user.TwoFactorEnabled || user.TwoFactorSecret is not string secret
            || (!string.IsNullOrWhiteSpace(oneTimeCode) && Application.Security.Totp.Verify(secret, oneTimeCode, now));
        if (!passwordOk || !codeOk)
        {
            user.RegisterFailedLogin(now, _options.MaxFailedAttempts, _options.LockoutDuration);
            bool locked = user.IsLockedOut(now);
            if (locked)
            {
                login.End(now);
            }

            _db.LoginEvents.Add(new LoginEvent(user.Id, user.Username, now, login.Terminal, "unlock", locked ? AuthenticationOutcome.LockedOut.ToString() : AuthenticationOutcome.InvalidCredentials.ToString(), null));
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Failure(locked
                ? Error.Rule("Security.SessionEnded", "Too many attempts; the account is locked and the session ended.")
                : Error.Rule("Security.Reauthenticate", passwordOk ? "The authenticator code is not right." : "The password is not right."));
        }

        user.RegisterSuccessfulLogin(now);
        login.Touch(now);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<(string Secret, string ProvisioningUri)>> BeginTwoFactorAsync(long userId, CancellationToken ct = default)
    {
        User? user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user is null)
        {
            return Error.NotFound("Security.UserNotFound", $"User {userId} not found.");
        }

        string secret = Application.Security.Totp.NewSecret();
        user.BeginTwoFactor(secret);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (secret, Application.Security.Totp.ProvisioningUri(_options.TwoFactorIssuer, user.Username, secret));
    }

    /// <inheritdoc />
    public async Task<Result> ConfirmTwoFactorAsync(long userId, string code, CancellationToken ct = default)
    {
        User? user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user?.TwoFactorSecret is not string secret)
        {
            return Result.Failure(Error.Rule("Security.TwoFactorNotStarted", "Start authenticator enrolment first."));
        }

        if (!Application.Security.Totp.Verify(secret, code, _clock.UtcNow))
        {
            return Result.Failure(Error.Rule("Security.TwoFactorCode", "The code does not match; check the device's clock and try the next code."));
        }

        user.SetTwoFactor(true);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> DisableTwoFactorAsync(long userId, CancellationToken ct = default)
    {
        User? user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("Security.UserNotFound", $"User {userId} not found."));
        }

        user.ClearTwoFactor();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    // Sessions still open: not ended, and (with an idle timeout) active within it.
    private async Task<List<LoginEvent>> OpenSessionsAsync(long userId, DateTimeOffset now, CancellationToken ct)
    {
        List<LoginEvent> open = await _db.LoginEvents.Where(l => l.UserId == userId && l.SessionId != null && l.EndedOnUtc == null).ToListAsync(ct).ConfigureAwait(false);
        return _options.SessionIdleTimeout is TimeSpan idle ? [.. open.Where(l => (l.LastSeenUtc ?? l.OccurredOnUtc) + idle >= now)] : open;
    }

    // A sign-in or sign-out in the system chain of the change log.
    private async Task<Core.Audit.ChangeLogEntry> LoginEntryAsync(long? userId, string userName, Core.Audit.AuditAction action, string? terminal, string sessionId, string method, CancellationToken ct)
    {
        // An entry added in this unit of work but not yet saved comes last; otherwise the newest saved entry.
        Core.Audit.ChangeLogEntry? pending = _db.ChangeLog.Local.LastOrDefault(c => c.CompanyId == null && c.Id == 0);
        string previous = pending?.RowHash ?? await _db.ChangeLog.Where(c => c.CompanyId == null).OrderByDescending(c => c.Id).Select(c => c.RowHash).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? string.Empty;
        return Core.Audit.ChangeLogEntry.Create(previous, _clock.UtcNow, null, userId, userName, "Security", "Session", sessionId, action, reason: method, terminal: terminal, sessionId: sessionId);
    }
}
