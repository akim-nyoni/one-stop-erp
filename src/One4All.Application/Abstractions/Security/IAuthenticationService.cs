using One4All.Core.Common;
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

    /// <summary>The licence's named-user limit leaves no room for this account.</summary>
    NotLicensed = 4,

    /// <summary>The password was right; the authenticator code is needed (or was wrong).</summary>
    TwoFactorRequired = 5,

    /// <summary>The user already has as many open sessions as allowed.</summary>
    TooManySessions = 6,
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

    /// <summary>
    /// Signs in with a password and, for a user with two-factor sign-in, the authenticator code: without a code the outcome is
    /// <see cref="AuthenticationOutcome.TwoFactorRequired"/> (not a failed attempt); a wrong code counts as one.
    /// </summary>
    /// <param name="username">The login name.</param>
    /// <param name="password">The password.</param>
    /// <param name="oneTimeCode">The authenticator code.</param>
    /// <param name="terminal">The machine or IP.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The outcome.</returns>
    Task<AuthenticationResult> AuthenticateWithCodeAsync(string username, string password, string? oneTimeCode, string? terminal = null, CancellationToken ct = default);

    /// <summary>
    /// Records activity on a session and says whether it is still open: a session idle longer than the timeout (or ended) is closed, so
    /// the client locks and asks the user to sign in again.
    /// </summary>
    /// <param name="sessionId">The session.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True while the session is open.</returns>
    Task<bool> TouchSessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Confirms the user of an open session is still at the keyboard: their password (and authenticator code when two-factor is on).
    /// Unlocks an idle-locked desktop, and steps up before a sensitive change. A wrong answer counts towards the account lockout, and
    /// a lockout ends the session.
    /// </summary>
    /// <param name="sessionId">The session.</param>
    /// <param name="password">The user's password.</param>
    /// <param name="oneTimeCode">The authenticator code, when two-factor is on.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why not (<c>Security.SessionEnded</c> means signing in again).</returns>
    Task<Result> ReauthenticateAsync(string sessionId, string password, string? oneTimeCode, CancellationToken ct = default);

    /// <summary>Starts authenticator enrolment: a new secret and the otpauth URI to scan; two-factor turns on once a code confirms it.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The secret and URI.</returns>
    Task<Result<(string Secret, string ProvisioningUri)>> BeginTwoFactorAsync(long userId, CancellationToken ct = default);

    /// <summary>Confirms enrolment with a code from the authenticator, turning two-factor on.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="code">The code.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> ConfirmTwoFactorAsync(long userId, string code, CancellationToken ct = default);

    /// <summary>Turns two-factor off (an administrator reset when a device is lost).</summary>
    /// <param name="userId">The user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> DisableTwoFactorAsync(long userId, CancellationToken ct = default);

    /// <summary>Authenticates a user by username and POS PIN.</summary>
    /// <param name="username">The login name.</param>
    /// <param name="pin">The plaintext PIN.</param>
    /// <param name="terminal">The originating terminal, for audit.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The authentication result.</returns>
    Task<AuthenticationResult> AuthenticateByPinAsync(string username, string pin, string? terminal = null, CancellationToken ct = default);

    /// <summary>Authenticates a cashier by the card they swiped or scanned (the till's card login).</summary>
    Task<AuthenticationResult> AuthenticateByCardAsync(string cardRead, string? terminal = null, CancellationToken ct = default);

    /// <summary>Authenticates a cashier by the enrolment the fingerprint reader matched.</summary>
    Task<AuthenticationResult> AuthenticateByBiometricAsync(string biometricId, string? terminal = null, CancellationToken ct = default);

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
