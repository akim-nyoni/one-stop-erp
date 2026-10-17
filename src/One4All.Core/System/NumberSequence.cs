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
    private readonly List<NumberSequenceYear> _years = [];

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

    /// <summary>
    /// The year most recently allocated in (for a yearly-reset sequence). Together with <see cref="NextValue"/> it
    /// mirrors that year's counter, so every allocation changes this row and the optimistic-concurrency check on it
    /// serialises concurrent allocators.
    /// </summary>
    public int CurrentYear { get; private set; }

    /// <summary>
    /// The next value to allocate: the single counter of a continuous sequence, or a mirror of
    /// <see cref="CurrentYear"/>'s counter for a yearly-reset sequence.
    /// </summary>
    public long NextValue { get; private set; }

    /// <summary>
    /// The per-year counters of a yearly-reset sequence (<c>sys.number_sequence_year</c>). Each year keeps its own
    /// counter, so posting back into an earlier, still-open year continues that year's numbering instead of
    /// restarting at 1 and colliding with numbers already issued.
    /// </summary>
    public IReadOnlyCollection<NumberSequenceYear> Years => _years.AsReadOnly();

    /// <summary>
    /// Allocates and returns the next formatted document number, incrementing the counter. A yearly-reset
    /// sequence draws from <paramref name="year"/>'s own counter (starting at 1 the first time that year is used).
    /// </summary>
    /// <param name="year">The document's year (used only when <see cref="ResetYearly"/> is set).</param>
    /// <returns>The formatted number, e.g. <c>INV-2026-000042</c>.</returns>
    /// <summary>Sets how the sequence numbers (brief §2.4: prefix, padding, year reset, gapless) without touching what it has allocated.</summary>
    /// <param name="prefix">The prefix (up to 20 characters).</param>
    /// <param name="padding">The digits the number is padded to (1–12).</param>
    /// <param name="resetYearly">Whether the counter starts again each year (the year goes into the number).</param>
    /// <param name="gapless">Whether the sequence must have no gaps (allocated inside the document's own unit of work, so a failed posting gives its number back).</param>
    public void Configure(string prefix, int padding, bool resetYearly, bool gapless)
    {
        if (prefix is null || prefix.Length > 20)
        {
            throw new ArgumentException("The prefix is up to 20 characters.", nameof(prefix));
        }

        if (padding is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(padding), "The padding is 1 to 12 digits.");
        }

        Prefix = prefix;
        Padding = padding;
        ResetYearly = resetYearly;
        Gapless = gapless;
    }

    /// <summary>Moves the next number forward (never back: numbers already given out stay unique).</summary>
    /// <param name="nextValue">The next number to give out.</param>
    /// <returns>True when moved; false when the sequence is already past it.</returns>
    public bool AdvanceTo(long nextValue)
    {
        if (nextValue < NextValue)
        {
            return false;
        }

        NextValue = nextValue;
        if (ResetYearly && _years.FirstOrDefault(y => y.Year == CurrentYear) is NumberSequenceYear counter)
        {
            counter.AdvanceTo(nextValue);
        }

        return true;
    }

    public string Allocate(int year)
    {
        long value;
        if (ResetYearly)
        {
            NumberSequenceYear? counter = _years.FirstOrDefault(y => y.Year == year);
            if (counter is null)
            {
                // A sequence created before per-year counters existed carries its live year only on this row.
                counter = new NumberSequenceYear(year, year == CurrentYear && NextValue > 1 ? NextValue : 1);
                _years.Add(counter);
            }

            value = counter.Take();
            CurrentYear = year;
            NextValue = counter.NextValue;
        }
        else
        {
            value = NextValue;
            NextValue++;
        }

        string number = value.ToString(global::System.Globalization.CultureInfo.InvariantCulture).PadLeft(Padding, '0');
        return ResetYearly ? $"{Prefix}{year}-{number}" : $"{Prefix}{number}";
    }
}

/// <summary>One year's counter within a yearly-reset <see cref="NumberSequence"/>.</summary>
public sealed class NumberSequenceYear
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private NumberSequenceYear()
    {
    }

    /// <summary>Creates a year counter.</summary>
    /// <param name="year">The year.</param>
    /// <param name="nextValue">The first value to allocate.</param>
    public NumberSequenceYear(int year, long nextValue)
    {
        Year = year;
        NextValue = nextValue;
    }

    /// <summary>The year this counter numbers.</summary>
    public int Year { get; private set; }

    /// <summary>The next value to allocate in this year.</summary>
    public long NextValue { get; private set; }

    /// <summary>Moves this year's next number forward (never back).</summary>
    /// <param name="nextValue">The next number.</param>
    public void AdvanceTo(long nextValue) => NextValue = Math.Max(NextValue, nextValue);

    /// <summary>Returns the next value and advances the counter.</summary>
    /// <returns>The allocated value.</returns>
    internal long Take() => NextValue++;
}
