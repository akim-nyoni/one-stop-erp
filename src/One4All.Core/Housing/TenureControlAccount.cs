using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>The receivables control account instalment debtors of a tenure type post to (<c>hse.tenure_control_account</c>; brief §4.10.9).</summary>
public sealed class TenureControlAccount : AuditableEntity, ICompanyScoped
{
    private TenureControlAccount() => AccountCode = string.Empty;

    /// <summary>Maps a tenure type to its control account.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="tenure">The tenure type.</param>
    /// <param name="accountCode">The receivables control account.</param>
    public TenureControlAccount(long companyId, InstalmentTenure tenure, string accountCode)
    {
        CompanyId = companyId;
        Tenure = tenure;
        AccountCode = accountCode;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets the tenure type.</summary>
    public InstalmentTenure Tenure { get; private set; }

    /// <summary>Gets or sets the control account.</summary>
    public string AccountCode { get; set; }
}
