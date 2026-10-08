using One4All.Core.Common;

namespace One4All.Core.Workflow;

/// <summary>
/// A single-use link emailed to an approver (<c>wf.action_token</c>, brief §7: "approve from the email"): only the token's SHA-256 is kept; it lets that
/// approver act on that stage of that workflow once, until it expires.
/// </summary>
public sealed class WorkflowActionToken : Entity, ICompanyScoped
{
    private WorkflowActionToken()
    {
        TokenHash = string.Empty;
    }

    /// <summary>Issues a token.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="instanceId">The workflow.</param>
    /// <param name="userId">The approver.</param>
    /// <param name="stageSequence">The stage it acts on.</param>
    /// <param name="tokenHash">The token's SHA-256 (hex).</param>
    /// <param name="expiresOnUtc">When it expires.</param>
    public WorkflowActionToken(long companyId, long instanceId, long userId, int stageSequence, string tokenHash, DateTimeOffset expiresOnUtc)
    {
        CompanyId = companyId;
        WorkflowInstanceId = instanceId;
        UserId = userId;
        StageSequence = stageSequence;
        TokenHash = tokenHash;
        ExpiresOnUtc = expiresOnUtc;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the workflow.</summary>
    public long WorkflowInstanceId { get; private set; }

    /// <summary>Gets the approver.</summary>
    public long UserId { get; private set; }

    /// <summary>Gets the stage it acts on.</summary>
    public int StageSequence { get; private set; }

    /// <summary>Gets the token's SHA-256 (hex).</summary>
    public string TokenHash { get; private set; }

    /// <summary>Gets when it expires.</summary>
    public DateTimeOffset ExpiresOnUtc { get; private set; }

    /// <summary>Gets or sets when it was used.</summary>
    public DateTimeOffset? UsedOnUtc { get; set; }

    /// <summary>The SHA-256 (lower-case hex) of a token as sent.</summary>
    /// <param name="token">The token.</param>
    /// <returns>The hash.</returns>
    public static string Hash(string token)
        => Convert.ToHexStringLower(global::System.Security.Cryptography.SHA256.HashData(global::System.Text.Encoding.UTF8.GetBytes(token ?? string.Empty)));
}
