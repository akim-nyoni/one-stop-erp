using One4All.Core.Common;

namespace One4All.Core.Inventory;

/// <summary>What a concrete test measures.</summary>
public enum ConcreteTestKind
{
    /// <summary>Slump at the plant or on site, in millimetres (passes within the tolerance of the target).</summary>
    Slump = 0,

    /// <summary>Cube compressive strength at 7 days, in MPa (passes at or above the specified strength).</summary>
    Cube7Day = 1,

    /// <summary>Cube compressive strength at 28 days, in MPa (passes at or above the specified strength).</summary>
    Cube28Day = 2,
}

/// <summary>
/// A slump or strength test on a concrete batch run (<c>inv.concrete_test</c>; brief §4.6 concrete batching: slump/strength test records),
/// with the result against what the mix specifies.
/// </summary>
public sealed class ConcreteTest : AuditableEntity, ICompanyScoped
{
    private ConcreteTest()
    {
        RunNumber = string.Empty;
        SampleReference = string.Empty;
    }

    /// <summary>Records a test.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="runNumber">The batch run sampled.</param>
    /// <param name="sampleReference">The sample or cube reference.</param>
    /// <param name="kind">What was tested.</param>
    /// <param name="testedOn">When.</param>
    /// <param name="result">The result (mm or MPa).</param>
    /// <param name="specified">The target slump or the specified strength.</param>
    /// <param name="tolerance">For slump: the allowed difference from the target (mm).</param>
    public ConcreteTest(long companyId, string runNumber, string sampleReference, ConcreteTestKind kind, DateOnly testedOn, decimal result, decimal specified, decimal tolerance)
    {
        CompanyId = companyId;
        RunNumber = runNumber;
        SampleReference = sampleReference;
        Kind = kind;
        TestedOn = testedOn;
        Result = result;
        Specified = specified;
        Tolerance = tolerance;
        Passed = Passes(kind, result, specified, tolerance);
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets the batch run sampled.</summary>
    public string RunNumber { get; private set; }

    /// <summary>Gets the sample or cube reference.</summary>
    public string SampleReference { get; private set; }

    /// <summary>Gets what was tested.</summary>
    public ConcreteTestKind Kind { get; private set; }

    /// <summary>Gets when.</summary>
    public DateOnly TestedOn { get; private set; }

    /// <summary>Gets the result (mm for slump, MPa for strength).</summary>
    public decimal Result { get; private set; }

    /// <summary>Gets the target slump or specified strength.</summary>
    public decimal Specified { get; private set; }

    /// <summary>Gets the slump tolerance.</summary>
    public decimal Tolerance { get; private set; }

    /// <summary>Gets whether it passed.</summary>
    public bool Passed { get; private set; }

    /// <summary>Gets or sets who tested it (laboratory or technician).</summary>
    public string? TestedBy { get; set; }

    /// <summary>Whether a result passes: slump within the tolerance of the target; strength at or above the specified.</summary>
    /// <param name="kind">What was tested.</param>
    /// <param name="result">The result.</param>
    /// <param name="specified">The target or specified value.</param>
    /// <param name="tolerance">The slump tolerance.</param>
    /// <returns>True when it passes.</returns>
    public static bool Passes(ConcreteTestKind kind, decimal result, decimal specified, decimal tolerance)
        => kind == ConcreteTestKind.Slump ? Math.Abs(result - specified) <= tolerance : result >= specified;
}
