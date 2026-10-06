using One4All.Core.Common;

namespace One4All.Core.Security;

/// <summary>
/// A named user of the system (<c>sec.user</c>). Holds authentication material (password/PIN hashes are
/// computed by the infrastructure with Argon2id — the entity stores only the resulting hash), lockout
/// state, and the assignment/override/group collections that feed permission resolution.
/// </summary>
public sealed class User : SoftDeletableEntity
{
    private readonly List<UserRole> _roles = [];
    private readonly List<UserGroupMember> _groups = [];
    private readonly List<UserPermissionOverrideEntity> _overrides = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private User()
    {
        Username = string.Empty;
        NormalizedUsername = string.Empty;
        DisplayName = string.Empty;
    }

    /// <summary>Creates a user.</summary>
    /// <param name="username">The unique login name.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="email">Optional email address.</param>
    public User(string username, string displayName, string? email = null)
    {
        Username = username;
        NormalizedUsername = username.Trim().ToUpperInvariant();
        DisplayName = displayName;
        Email = email;
        IsActive = true;
        MustChangePassword = true;
    }

    /// <summary>The login name.</summary>
    public string Username { get; private set; }

    /// <summary>Upper-cased login name for case-insensitive uniqueness.</summary>
    public string NormalizedUsername { get; private set; }

    /// <summary>The display name shown in the UI.</summary>
    public string DisplayName { get; set; }

    /// <summary>Optional email address (for statements, notifications, password reset).</summary>
    public string? Email { get; set; }

    /// <summary>The Argon2id password hash, or null if not yet set.</summary>
    public string? PasswordHash { get; private set; }

    /// <summary>The Argon2id PIN hash for fast POS login, or null if not set.</summary>
    public string? PinHash { get; private set; }

    /// <summary>Random value rotated on credential change; invalidates existing sessions.</summary>
    public string SecurityStamp { get; private set; } = Guid.NewGuid().ToString("N");

    /// <summary>Whether the account is active. Inactive users cannot sign in.</summary>
    public bool IsActive { get; private set; }

    /// <summary>The built-in system administrator bypasses per-permission checks (but is still audited).</summary>
    public bool IsSystemAdministrator { get; private set; }

    /// <summary>Whether the user must change their password at next login.</summary>
    public bool MustChangePassword { get; private set; }

    /// <summary>Whether TOTP two-factor authentication is enabled (back-office roles).</summary>
    public bool TwoFactorEnabled { get; private set; }

    /// <summary>Consecutive failed sign-in attempts, reset on success.</summary>
    public int FailedAccessCount { get; private set; }

    /// <summary>UTC instant until which the account is locked out, or null if not locked.</summary>
    public DateTimeOffset? LockoutEndUtc { get; private set; }

    /// <summary>UTC instant of the last successful sign-in.</summary>
    public DateTimeOffset? LastLoginUtc { get; private set; }

    /// <summary>Direct role assignments.</summary>
    public IReadOnlyCollection<UserRole> Roles => _roles.AsReadOnly();

    /// <summary>Group memberships.</summary>
    public IReadOnlyCollection<UserGroupMember> Groups => _groups.AsReadOnly();

    /// <summary>Explicit permission overrides.</summary>
    public IReadOnlyCollection<UserPermissionOverrideEntity> Overrides => _overrides.AsReadOnly();

    /// <summary>True when the account is currently locked out.</summary>
    /// <param name="nowUtc">The current time.</param>
    /// <returns>True when locked.</returns>
    public bool IsLockedOut(DateTimeOffset nowUtc) => LockoutEndUtc is { } end && end > nowUtc;

    /// <summary>Sets the password hash and rotates the security stamp.</summary>
    /// <param name="passwordHash">The Argon2id hash computed by the infrastructure.</param>
    /// <param name="mustChange">Whether the user must change it at next login.</param>
    public void SetPasswordHash(string passwordHash, bool mustChange)
    {
        PasswordHash = passwordHash;
        MustChangePassword = mustChange;
        RotateSecurityStamp();
    }

    /// <summary>Sets the PIN hash used for POS login.</summary>
    /// <param name="pinHash">The Argon2id hash of the PIN.</param>
    public void SetPinHash(string? pinHash) => PinHash = pinHash;

    /// <summary>Marks the account as (or not as) the built-in system administrator.</summary>
    /// <param name="value">True to grant system-administrator status.</param>
    public void SetSystemAdministrator(bool value) => IsSystemAdministrator = value;

    /// <summary>Enables or disables the account.</summary>
    /// <param name="active">True to activate.</param>
    public void SetActive(bool active) => IsActive = active;

    /// <summary>Clears a lockout and the failed-attempt count (after an administrator's password reset).</summary>
    public void Unlock()
    {
        FailedAccessCount = 0;
        LockoutEndUtc = null;
    }

    /// <summary>Enables or disables TOTP two-factor authentication.</summary>
    /// <param name="enabled">True to enable.</param>
    public void SetTwoFactor(bool enabled) => TwoFactorEnabled = enabled;

    /// <summary>Records a successful sign-in: clears failures and stamps the time.</summary>
    /// <param name="nowUtc">The current time.</param>
    public void RegisterSuccessfulLogin(DateTimeOffset nowUtc)
    {
        FailedAccessCount = 0;
        LockoutEndUtc = null;
        LastLoginUtc = nowUtc;
    }

    /// <summary>
    /// Records a failed sign-in. After <paramref name="maxAttempts"/> consecutive failures the account
    /// is locked for <paramref name="lockoutDuration"/>.
    /// </summary>
    /// <param name="nowUtc">The current time.</param>
    /// <param name="maxAttempts">Attempts allowed before lockout.</param>
    /// <param name="lockoutDuration">How long to lock the account.</param>
    public void RegisterFailedLogin(DateTimeOffset nowUtc, int maxAttempts, TimeSpan lockoutDuration)
    {
        FailedAccessCount++;
        if (FailedAccessCount >= maxAttempts)
        {
            LockoutEndUtc = nowUtc.Add(lockoutDuration);
        }
    }

    /// <summary>Assigns a role directly to the user if not already assigned; returns the assignment.</summary>
    /// <param name="roleId">The role id.</param>
    /// <returns>The existing or newly created assignment.</returns>
    public UserRole AssignRole(long roleId)
    {
        UserRole? existing = _roles.FirstOrDefault(r => r.RoleId == roleId);
        if (existing is not null)
        {
            return existing;
        }

        UserRole assignment = new(roleId);
        _roles.Add(assignment);
        return assignment;
    }

    /// <summary>Removes a direct role assignment.</summary>
    /// <param name="roleId">The role id.</param>
    public void RemoveRole(long roleId) => _roles.RemoveAll(r => r.RoleId == roleId);

    /// <summary>Adds the user to a group if not already a member.</summary>
    /// <param name="groupId">The group id.</param>
    public void JoinGroup(long groupId)
    {
        if (_groups.All(g => g.GroupId != groupId))
        {
            _groups.Add(new UserGroupMember(groupId));
        }
    }

    /// <summary>Removes the user from a group.</summary>
    /// <param name="groupId">The group id.</param>
    public void LeaveGroup(long groupId) => _groups.RemoveAll(g => g.GroupId == groupId);

    /// <summary>Adds or replaces an explicit override for a permission code.</summary>
    /// <param name="permissionCode">The <c>Module.Feature</c> code.</param>
    /// <param name="operations">The operations affected.</param>
    /// <param name="isGrant">True to grant, false to deny.</param>
    public void SetOverride(string permissionCode, OperationFlags operations, bool isGrant)
    {
        _overrides.RemoveAll(o =>
            string.Equals(o.PermissionCode, permissionCode, StringComparison.OrdinalIgnoreCase) &&
            o.IsGrant == isGrant);
        _overrides.Add(new UserPermissionOverrideEntity(permissionCode, operations, isGrant));
    }

    private void RotateSecurityStamp() => SecurityStamp = Guid.NewGuid().ToString("N");
}
