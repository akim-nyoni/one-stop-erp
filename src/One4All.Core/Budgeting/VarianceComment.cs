using One4All.Core.Common;

namespace One4All.Core.Budgeting;

/// <summary>
/// A budget holder's explanation of a variance (<c>bud.variance_comment</c>, brief §4.18.6): one per account for a year
/// to an as-at period, printed with the management accounts so the month-end pack carries the reasons, not just the
/// numbers. A later comment for the same account and period replaces the earlier one; the audit trail keeps both.
/// </summary>
public sealed class VarianceComment : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private VarianceComment()
    {
        AccountCode = string.Empty;
        Comment = string.Empty;
    }

    /// <summary>Records an explanation.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fiscalYearId">The fiscal year.</param>
    /// <param name="asAtPeriod">The period the variance is to.</param>
    /// <param name="accountCode">The account.</param>
    /// <param name="comment">The explanation.</param>
    /// <param name="authorUserId">Who wrote it.</param>
    public VarianceComment(long companyId, long fiscalYearId, int asAtPeriod, string accountCode, string comment, long authorUserId)
    {
        CompanyId = companyId;
        FiscalYearId = fiscalYearId;
        AsAtPeriod = asAtPeriod;
        AccountCode = accountCode;
        Comment = comment;
        AuthorUserId = authorUserId;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The fiscal year.</summary>
    public long FiscalYearId { get; private set; }

    /// <summary>The period the variance is to.</summary>
    public int AsAtPeriod { get; private set; }

    /// <summary>The account.</summary>
    public string AccountCode { get; private set; }

    /// <summary>The explanation.</summary>
    public string Comment { get; private set; }

    /// <summary>Who wrote it.</summary>
    public long AuthorUserId { get; private set; }

    /// <summary>Replaces the explanation.</summary>
    /// <param name="comment">The new explanation.</param>
    /// <param name="authorUserId">Who wrote it.</param>
    public void Revise(string comment, long authorUserId)
    {
        Comment = comment;
        AuthorUserId = authorUserId;
    }
}
