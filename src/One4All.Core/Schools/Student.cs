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

    /// <summary>The staff member whose child this is (fees take the staff-child discount while they are employed); null: none.</summary>
    public long? StaffEmployeeId { get; set; }

    /// <summary>Do not disclose: the student's identity is withheld from reports for anyone without the restricted-records permission.</summary>
    public bool DoNotDisclose { get; private set; }

    /// <summary>Why (court order, protection…), seen only by those allowed.</summary>
    public string? DisclosureNote { get; private set; }

    /// <summary>Sets or lifts do-not-disclose.</summary>
    /// <param name="doNotDisclose">Whether to withhold.</param>
    /// <param name="note">Why.</param>
    public void SetDisclosure(bool doNotDisclose, string? note)
    {
        DoNotDisclose = doNotDisclose;
        DisclosureNote = doNotDisclose ? note?.Trim() : null;
    }

    /// <summary>The name a reader may see: withheld for a do-not-disclose student unless they may see restricted records.</summary>
    /// <param name="canSeeRestricted">Whether the reader may see restricted records.</param>
    /// <returns>The name, or a placeholder.</returns>
    public string ShownName(bool canSeeRestricted) => !DoNotDisclose || canSeeRestricted ? FullName : "Restricted record";

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

    /// <summary>When the student joined (a later date than the term start is billed pro rata).</summary>
    public DateOnly? AdmittedOn { get; set; }

    /// <summary>When the student left, once withdrawn.</summary>
    public DateOnly? LeftOn { get; private set; }

    /// <summary>Withdraws the student from a date.</summary>
    /// <param name="leftOn">The last day enrolled.</param>
    public void Withdraw(DateOnly leftOn)
    {
        IsEnrolled = false;
        LeftOn = leftOn;
    }

    /// <summary>The bursary, then the sibling percentage on what is left.</summary>
    /// <param name="compulsoryTotal">The compulsory fees.</param>
    /// <param name="siblingPercent">The sibling discount percentage.</param>
    /// <returns>The bursary and the sibling discount.</returns>
    public (decimal Bursary, decimal Sibling) ComputeDiscounts(decimal compulsoryTotal, decimal siblingPercent)
    {
        decimal bursary = ComputeDiscount(compulsoryTotal);
        decimal sibling = decimal.Round((compulsoryTotal - bursary) * siblingPercent / 100m, 2, MidpointRounding.AwayFromZero);
        return (bursary, sibling);
    }

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

/// <summary>
/// A sibling discount (<c>sch.sibling_discount</c>): from the given child of a family (students sharing a guardian, in admission order),
/// a percentage off the compulsory fees left after any bursary. The rule with the highest child number not above a student's applies.
/// </summary>
public sealed class SiblingDiscount : Entity, ICompanyScoped
{
    private SiblingDiscount()
    {
    }

    /// <summary>Creates the rule.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fromChild">The child it starts at (2 = the second child).</param>
    /// <param name="percent">The percentage off.</param>
    public SiblingDiscount(long companyId, int fromChild, decimal percent)
    {
        CompanyId = companyId;
        FromChild = fromChild;
        Percent = percent;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the child it starts at.</summary>
    public int FromChild { get; private set; }

    /// <summary>Gets or sets the percentage off.</summary>
    public decimal Percent { get; set; }

    /// <summary>The sibling percentage for a child: the rule with the highest starting child not above it (0 when none applies).</summary>
    /// <param name="rules">The company's rules.</param>
    /// <param name="childOrdinal">The child's place in the family (1 = first).</param>
    /// <returns>The percentage.</returns>
    public static decimal PercentFor(IEnumerable<SiblingDiscount> rules, int childOrdinal)
        => rules.Where(r => r.FromChild <= childOrdinal).OrderByDescending(r => r.FromChild).Select(r => r.Percent).FirstOrDefault();
}

