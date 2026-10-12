using One4All.Core.Common;
using One4All.Core.Inventory;

namespace One4All.Application.Abstractions.Inventory;

/// <summary>Slump and cube-strength tests on concrete batch runs, judged against the mix's targets.</summary>
public interface IConcreteTestService
{
    /// <summary>Records a test on a batch run (the run must exist); the result is judged at once.</summary>
    /// <returns>The test's id and whether it passed.</returns>
    Task<Result<(long Id, bool Passed)>> RecordAsync(long companyId, ConcreteTestEntry entry, CancellationToken ct = default);

    /// <summary>The tests on one batch run, by date.</summary>
    Task<IReadOnlyList<ConcreteTest>> ForRunAsync(long companyId, string runNumber, CancellationToken ct = default);

    /// <summary>The tests that failed between two dates (for follow-up with the customer and the plant).</summary>
    Task<IReadOnlyList<ConcreteTest>> FailuresAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default);
}

/// <summary>A test to record.</summary>
/// <param name="RunNumber">The batch run.</param>
/// <param name="SampleReference">The sample or cube reference.</param>
/// <param name="Kind">Slump, or cube strength at 7 or 28 days.</param>
/// <param name="TestedOn">When.</param>
/// <param name="Result">The result (mm or MPa).</param>
/// <param name="Specified">The target slump or specified strength.</param>
/// <param name="Tolerance">The slump tolerance (mm).</param>
public sealed record ConcreteTestEntry(string RunNumber, string SampleReference, ConcreteTestKind Kind, DateOnly TestedOn, decimal Result, decimal Specified, decimal Tolerance = 0m)
{
    /// <summary>Who tested it.</summary>
    public string? TestedBy { get; init; }
}
