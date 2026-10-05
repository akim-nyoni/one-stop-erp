using One4All.Application.Abstractions.Security;

namespace One4All.Desktop;

/// <summary>
/// The signed-in user of this desktop instance, set once at login, so windows can act — and be audited — as that user
/// (for example the report viewer's previews and exports in the report run log).
/// </summary>
public sealed class DesktopSession
{
    /// <summary>The signed-in user, once someone has signed in.</summary>
    public long? UserId { get; private set; }

    /// <summary>The signed-in user's name.</summary>
    public string? UserName { get; private set; }

    /// <summary>The sign-in session, ended when the application closes.</summary>
    public string? SessionId { get; private set; }

    /// <summary>Records a successful sign-in.</summary>
    /// <param name="result">The authentication result.</param>
    public void SignIn(AuthenticationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        UserId = result.UserId;
        UserName = result.UserName;
        SessionId = result.SessionId;
    }
}
