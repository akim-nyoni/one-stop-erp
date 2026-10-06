using One4All.Core.Common;

namespace One4All.Core.Security;

/// <summary>
/// One sign-in attempt (<c>sec.login_event</c>): who, from where, how and what happened. A successful attempt opens a session that is
/// closed when the user signs out.
/// </summary>
public sealed class LoginEvent : Entity
{
    private LoginEvent()
    {
        Username = string.Empty;
        Method = string.Empty;
        Outcome = string.Empty;
    }

    /// <summary>Records an attempt.</summary>
    /// <param name="userId">The user, when the name matched one.</param>
    /// <param name="username">The name typed.</param>
    /// <param name="occurredOnUtc">When.</param>
    /// <param name="terminal">The machine or channel.</param>
    /// <param name="method">Password or PIN.</param>
    /// <param name="outcome">What happened.</param>
    /// <param name="sessionId">The session opened, on success.</param>
    public LoginEvent(long? userId, string username, DateTimeOffset occurredOnUtc, string? terminal, string method, string outcome, string? sessionId)
    {
        UserId = userId;
        Username = username;
        OccurredOnUtc = occurredOnUtc;
        Terminal = terminal;
        Method = method;
        Outcome = outcome;
        SessionId = sessionId;
    }

    /// <summary>Gets the user.</summary>
    public long? UserId { get; private set; }

    /// <summary>Gets the name typed.</summary>
    public string Username { get; private set; }

    /// <summary>Gets when.</summary>
    public DateTimeOffset OccurredOnUtc { get; private set; }

    /// <summary>Gets the machine or channel.</summary>
    public string? Terminal { get; private set; }

    /// <summary>Gets the method (Password, PIN).</summary>
    public string Method { get; private set; }

    /// <summary>Gets the outcome (Success, InvalidCredentials, LockedOut, Inactive).</summary>
    public string Outcome { get; private set; }

    /// <summary>Gets the session opened.</summary>
    public string? SessionId { get; private set; }

    /// <summary>Gets when the session ended.</summary>
    public DateTimeOffset? EndedOnUtc { get; private set; }

    /// <summary>Ends the session.</summary>
    /// <param name="at">When.</param>
    public void End(DateTimeOffset at) => EndedOnUtc ??= at;

    /// <summary>Gets when the session was last active (null: at sign-in).</summary>
    public DateTimeOffset? LastSeenUtc { get; private set; }

    /// <summary>Records activity on the session.</summary>
    /// <param name="at">When.</param>
    public void Touch(DateTimeOffset at) => LastSeenUtc = at;
}
