using One4All.Core.Security;

namespace One4All.Application.Abstractions.Security;

/// <summary>The outcome of an authentication attempt.</summary>
public enum AuthenticationOutcome
{
    /// <summary>Credentials accepted.</summary>
    Success = 0,

    /// <summary>Username unknown or password/PIN wrong.</summary>
    InvalidCredentials = 1,

    /// <summary>The account is locked out after too many failures.</summary>
    LockedOut = 2,

    /// <summary>The account is inactive.</summary>
    Inactive = 3,
}

/// <summary>The result of an authentication attempt, including the resolved permissions on success.</summary>
public sealed class AuthenticationResult
{
    private AuthenticationResult(AuthenticationOutcome outcome, string message)
    {
        Outcome = outcome;
        Message = message;
        Permissions = EffectivePermissions.None;
        UserName = string.Empty;
        DisplayName = string.Empty;
    }

    /// <summary>The outcome.</summary>
    public AuthenticationOutcome Outcome { get; private init; }

    /// <summary>A human-readable message for the login screen.</summary>
    public string Message { get; private init; }

    /// <summary>The authenticated user id (on success).</summary>
    public long UserId { get; private init; }

    /// <summary>The login name.</summary>
    public string UserName { get; private init; }

    /// <summary>The display name.</summary>
    public string DisplayName { get; private init; }

    /// <summary>Whether the user is the built-in system administrator.</summary>
    public bool IsSystemAdministrator { get; private init; }

    /// <summary>Whether the user must change their password before continuing.</summary>
    public bool MustChangePassword { get; private init; }

    /// <summary>The resolved permissions (on success).</summary>
    public EffectivePermissions Permissions { get; private init; }

    /// <summary>Gets the session opened by a successful sign-in, to end on sign-out.</summary>
    public string? SessionId { get; private init; }

    /// <summary>True when authentication succeeded.</summary>
    public bool Succeeded => Outcome == AuthenticationOutcome.Success;

    /// <summary>Creates a success result.</summary>
    /// <param name="userId">User id.</param>
    /// <param name="userName">Login name.</param>
    /// <param name="displayName">Display name.</param>
    /// <param name="isSystemAdministrator">System-administrator flag.</param>
    /// <param name="mustChangePassword">Must-change-password flag.</param>
    /// <param name="permissions">Resolved permissions.</param>
    /// <returns>The result.</returns>
    public static AuthenticationResult Success(
        long userId, string userName, string displayName, bool isSystemAdministrator,
        bool mustChangePassword, EffectivePermissions permissions, string? sessionId = null) =>
        new(AuthenticationOutcome.Success, mustChangePassword ? "Password change required." : "Signed in.")
        {
            UserId = userId,
            UserName = userName,
            DisplayName = displayName,
            IsSystemAdministrator = isSystemAdministrator,
            MustChangePassword = mustChangePassword,
            Permissions = permissions,
            SessionId = sessionId,
        };

    /// <summary>Creates a failure result.</summary>
    /// <param name="outcome">The failure outcome.</param>
    /// <param name="message">The message.</param>
    /// <returns>The result.</returns>
    public static AuthenticationResult Failure(AuthenticationOutcome outcome, string message) => new(outcome, message);
}

/// <summary>Authenticates users by password or PIN and enforces the lockout policy (brief §5.2).</summary>
public interface IAuthenticationService
{
    /// <summary>Authenticates a user by username and password.</summary>
    /// <param name="username">The login name.</param>
    /// <param name="password">The plaintext password.</param>
    /// <param name="terminal">The originating terminal, for audit.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The authentication result.</returns>
    Task<AuthenticationResult> AuthenticateAsync(string username, string password, string? terminal = null, CancellationToken ct = default);

    /// <summary>Authenticates a user by username and POS PIN.</summary>
    /// <param name="username">The login name.</param>
    /// <param name="pin">The plaintext PIN.</param>
    /// <param name="terminal">The originating terminal, for audit.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The authentication result.</returns>
    Task<AuthenticationResult> AuthenticateByPinAsync(string username, string pin, string? terminal = null, CancellationToken ct = default);

    /// <summary>Changes a user's password (rotating the security stamp).</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="currentPassword">The current password.</param>
    /// <param name="newPassword">The new password.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when the change succeeded.</returns>
    Task<bool> ChangePasswordAsync(long userId, string currentPassword, string newPassword, CancellationToken ct = default);

    /// <summary>Ends a session opened by a successful sign-in (sign-out); ending it again changes nothing.</summary>
    /// <param name="sessionId">The session.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    Task EndSessionAsync(string sessionId, CancellationToken ct = default);
}
