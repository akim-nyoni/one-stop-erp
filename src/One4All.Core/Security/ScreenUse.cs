using One4All.Core.Common;

namespace One4All.Core.Security;

/// <summary>
/// One use of a desktop screen: opened or run, by whom and when (brief §14 report 236 "screens used"). Written by the desktop console; read by the
/// user activity and screen usage reports.
/// </summary>
public sealed class ScreenUse : Entity
{
    private ScreenUse()
    {
        Module = string.Empty;
        Screen = string.Empty;
        Action = string.Empty;
    }

    /// <summary>Records a use.</summary>
    /// <param name="companyId">The company the screen was used in.</param>
    /// <param name="userId">The user.</param>
    /// <param name="module">The console module.</param>
    /// <param name="screen">The screen title.</param>
    /// <param name="action">"Open" or "Run".</param>
    /// <param name="occurredOnUtc">When.</param>
    public ScreenUse(long companyId, long userId, string module, string screen, string action, DateTimeOffset occurredOnUtc)
    {
        CompanyId = companyId;
        UserId = userId;
        Module = module;
        Screen = screen;
        Action = action;
        OccurredOnUtc = occurredOnUtc;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the user.</summary>
    public long UserId { get; private set; }

    /// <summary>Gets the console module.</summary>
    public string Module { get; private set; }

    /// <summary>Gets the screen title.</summary>
    public string Screen { get; private set; }

    /// <summary>Gets what was done: "Open" or "Run".</summary>
    public string Action { get; private set; }

    /// <summary>Gets when.</summary>
    public DateTimeOffset OccurredOnUtc { get; private set; }
}
