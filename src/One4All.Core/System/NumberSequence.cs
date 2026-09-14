using One4All.Core.Common;

namespace One4All.Core.System;

/// <summary>
/// A document-numbering sequence (<c>sys.number_sequence</c>), defined per company / branch / document
/// type with a prefix, zero-padding, optional yearly reset and a <b>gapless</b> mode for fiscal
/// compliance (brief §2.4). In gapless mode the next value is allocated inside the posting transaction
/// under a row lock, so a rolled-back posting does not burn a number.
/// </summary>
public sealed class NumberSequence : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private NumberSequence()
    {
        DocumentType = string.Empty;
        Prefix = string.Empty;
    }

    /// <summary>Creates a number sequence.</summary>
    /// <param name="companyId">The owning company.</param>
    /// <param name="documentType">The document type key (e.g. <c>AR.Invoice</c>).</param>
    /// <param name="prefix">Optional prefix (e.g. <c>INV-</c>).</param>
    /// <param name="padding">Minimum digit count (zero-padded).</param>
    /// <param name="branchId">Optional branch scope; null for a company-wide sequence.</param>
    /// <param name="resetYearly">Whether the counter resets at the start of each year.</param>
    /// <param name="gapless">Whether the sequence must be gapless.</param>
    public NumberSequence(
        long companyId,
        string documentType,
        string prefix,
        int padding,
        long? branchId = null,
        bool resetYearly = false,
        bool gapless = true)
    {
        CompanyId = companyId;
        DocumentType = documentType;
        Prefix = prefix;
        Padding = padding;
        BranchId = branchId;
        ResetYearly = resetYearly;
        Gapless = gapless;
        NextValue = 1;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Optional branch scope.</summary>
    public long? BranchId { get; private set; }

    /// <summary>The document type key.</summary>
    public string DocumentType { get; private set; }

    /// <summary>Optional prefix.</summary>
    public string Prefix { get; set; }

    /// <summary>Minimum digit count (zero-padded).</summary>
    public int Padding { get; set; }

    /// <summary>Whether the counter resets each year.</summary>
    public bool ResetYearly { get; set; }

    /// <summary>Whether the sequence must be gapless (fiscal compliance).</summary>
    public bool Gapless { get; private set; }

    /// <summary>The year the counter currently applies to (for yearly reset).</summary>
    public int CurrentYear { get; private set; }

    /// <summary>The next value to allocate.</summary>
    public long NextValue { get; private set; }

    /// <summary>
    /// Allocates and returns the next formatted document number, incrementing the counter. Resets the
    /// counter first when <see cref="ResetYearly"/> is set and the year has changed.
    /// </summary>
    /// <param name="year">The current accounting year (used only when <see cref="ResetYearly"/> is set).</param>
    /// <returns>The formatted number, e.g. <c>INV-2026-000042</c>.</returns>
    public string Allocate(int year)
    {
        if (ResetYearly && year != CurrentYear)
        {
            CurrentYear = year;
            NextValue = 1;
        }

        long value = NextValue;
        NextValue++;

        string number = value.ToString().PadLeft(Padding, '0');
        return ResetYearly ? $"{Prefix}{CurrentYear}-{number}" : $"{Prefix}{number}";
    }
}
