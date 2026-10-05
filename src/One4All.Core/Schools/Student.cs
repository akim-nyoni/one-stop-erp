using One4All.Core.Common;

namespace One4All.Core.Schools;

/// <summary>How a bursary/scholarship reduces a student's fees (brief §4.11).</summary>
public enum BursaryType
{
    /// <summary>No bursary.</summary>
    None = 0,

    /// <summary>A percentage of the compulsory fee total.</summary>
    Percentage = 1,

    /// <summary>A fixed value off the compulsory fee total.</summary>
    Value = 2,
}

/// <summary>
/// A student (brief §4.11, <c>sch.student</c>). Fees are billed to the guardian/sponsor account (a
/// customer), so the school's debtors, statements and ageing reuse the AR sub-ledger.
/// </summary>
public sealed class Student : SoftDeletableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Student()
    {
        AdmissionNumber = string.Empty;
        FullName = string.Empty;
        Grade = string.Empty;
    }

    /// <summary>Creates a student.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="admissionNumber">The admission number.</param>
    /// <param name="fullName">The full name.</param>
    /// <param name="grade">The current grade/form/class.</param>
    /// <param name="guardianCustomerId">The guardian/sponsor account billed for fees.</param>
    public Student(long companyId, string admissionNumber, string fullName, string grade, long guardianCustomerId)
    {
        CompanyId = companyId;
        AdmissionNumber = admissionNumber.Trim().ToUpperInvariant();
        FullName = fullName;
        Grade = grade;
        GuardianCustomerId = guardianCustomerId;
        IsEnrolled = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The admission number.</summary>
    public string AdmissionNumber { get; private set; }

    /// <summary>The full name.</summary>
    public string FullName { get; set; }

    /// <summary>The current grade/form/class.</summary>
    public string Grade { get; set; }

    /// <summary>The guardian/sponsor account billed for fees.</summary>
    public long GuardianCustomerId { get; set; }

    /// <summary>Whether the student is a boarder.</summary>
    public bool IsBoarder { get; set; }

    /// <summary>Gets or sets the sponsor (a customer account) billed for the student's fees instead of the guardian.</summary>
    public long? SponsorCustomerId { get; set; }

    /// <summary>Gets or sets the stream (class) within the grade.</summary>
    public string? Stream { get; set; }

    /// <summary>Gets or sets the transport route the student rides.</summary>
    public long? TransportRouteId { get; set; }

    /// <summary>Gets or sets the hostel a boarder lives in.</summary>
    public long? HostelId { get; set; }

    /// <summary>Gets or sets the most the student may spend from their wallet in a day (null: no limit).</summary>
    public decimal? DailySpendLimit { get; set; }

    /// <summary>Gets the account the student's fees are billed to: the sponsor when there is one, otherwise the guardian.</summary>
    public long PayerCustomerId => SponsorCustomerId ?? GuardianCustomerId;

    /// <summary>Whether the student is currently enrolled.</summary>
    public bool IsEnrolled { get; private set; }

    /// <summary>The bursary/scholarship type.</summary>
    public BursaryType BursaryType { get; private set; }

    /// <summary>The bursary value (percentage points or a fixed value, per <see cref="BursaryType"/>).</summary>
    public decimal BursaryValue { get; private set; }

    /// <summary>Grants a bursary/scholarship to the student.</summary>
    /// <param name="type">The bursary type.</param>
    /// <param name="value">The percentage or value.</param>
    public void GrantBursary(BursaryType type, decimal value)
    {
        BursaryType = type;
        BursaryValue = value;
    }

    /// <summary>Withdraws the student from enrolment.</summary>
    public void Withdraw() => IsEnrolled = false;

    /// <summary>Computes the discount for a given compulsory fee total.</summary>
    /// <param name="compulsoryTotal">The compulsory fee total.</param>
    /// <returns>The discount amount (never more than the total).</returns>
    public decimal ComputeDiscount(decimal compulsoryTotal)
    {
        decimal discount = BursaryType switch
        {
            BursaryType.Percentage => decimal.Round(compulsoryTotal * BursaryValue / 100m, 4, MidpointRounding.ToEven),
            BursaryType.Value => BursaryValue,
            _ => 0m,
        };

        return discount > compulsoryTotal ? compulsoryTotal : discount;
    }
}
