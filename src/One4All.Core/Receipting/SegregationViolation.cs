using One4All.Core.Common;

namespace One4All.Core.Receipting;

/// <summary>
/// A logged segregation-of-duties violation attempt (brief §RCP-15, <c>rcp.sod_violation</c>): a blocked
/// action such as a cashier trying to verify their own cash-up or reverse their own receipt. The action is
/// refused and the attempt is recorded for the SoD-violations report (RC22).
/// </summary>
public sealed class SegregationViolation : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private SegregationViolation() => Conflict = string.Empty;

    /// <summary>Creates a violation record.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="userId">The user who attempted the conflicting action.</param>
    /// <param name="conflict">A description of the SoD conflict.</param>
    /// <param name="occurredOn">The date of the attempt.</param>
    public SegregationViolation(long companyId, long userId, string conflict, DateOnly occurredOn)
    {
        CompanyId = companyId;
        UserId = userId;
        Conflict = conflict;
        OccurredOn = occurredOn;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The user who attempted the conflicting action.</summary>
    public long UserId { get; private set; }

    /// <summary>A description of the SoD conflict.</summary>
    public string Conflict { get; private set; }

    /// <summary>The date of the attempt.</summary>
    public DateOnly OccurredOn { get; private set; }
}
