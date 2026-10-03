using One4All.Application.Municipal;
using One4All.Core.Common;
using One4All.Core.Municipal;

namespace One4All.Application.Abstractions.Municipal;

/// <summary>A reading captured or estimated.</summary>
/// <param name="ReadingId">The reading record.</param>
/// <param name="Reading">The register value.</param>
/// <param name="Consumption">What the meter recorded (× multiplier).</param>
/// <param name="BilledConsumption">What to bill.</param>
/// <param name="Flags">What validation found.</param>
/// <param name="Accepted">Whether it was accepted.</param>
public sealed record ReadingOutcome(long ReadingId, decimal Reading, decimal Consumption, decimal BilledConsumption, ReadingFlags Flags, bool Accepted);

/// <summary>A meter replacement.</summary>
/// <param name="FinalReading">The old meter's final reading.</param>
/// <param name="NewMeterId">The new meter.</param>
/// <param name="ServicesRelinked">The property services moved to the new meter.</param>
public sealed record MeterReplacement(ReadingOutcome FinalReading, long NewMeterId, int ServicesRelinked);

/// <summary>A flagged reading on the exceptions report.</summary>
/// <param name="Serial">The meter.</param>
/// <param name="ReadingDate">The date.</param>
/// <param name="Kind">Actual or estimated.</param>
/// <param name="Reading">The register value.</param>
/// <param name="Consumption">The consumption recorded.</param>
/// <param name="Flags">What validation found.</param>
/// <param name="Accepted">Whether it was accepted.</param>
public sealed record ReadingException(string Serial, DateOnly ReadingDate, MeterReadingKind Kind, decimal Reading, decimal Consumption, ReadingFlags Flags, bool Accepted);

/// <summary>A line of a meter's consumption history.</summary>
/// <param name="ReadingDate">The date.</param>
/// <param name="Kind">Actual or estimated.</param>
/// <param name="Reading">The register value.</param>
/// <param name="Consumption">The consumption recorded.</param>
/// <param name="BilledConsumption">The consumption billed.</param>
/// <param name="Flags">What validation found.</param>
/// <param name="Accepted">Whether it was accepted.</param>
public sealed record ConsumptionLine(DateOnly ReadingDate, MeterReadingKind Kind, decimal Reading, decimal Consumption, decimal BilledConsumption, ReadingFlags Flags, bool Accepted);

/// <summary>
/// Meter readings (brief §4.9): install meters, capture validated readings, estimate missed ones, replace a meter with
/// its reading carried over, and report flagged readings and consumption history.
/// </summary>
public interface IMeterReadingService
{
    /// <summary>Installs a meter with its start reading.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="serial">The serial (unique).</param>
    /// <param name="digits">The number of dials (0 when unknown).</param>
    /// <param name="multiplier">The reading multiplier.</param>
    /// <param name="startReading">The register value at installation.</param>
    /// <param name="date">The installation date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The meter, or why it could not be installed.</returns>
    Task<Result<long>> InstallAsync(long companyId, string serial, int digits, decimal multiplier, decimal startReading, DateOnly date, CancellationToken ct = default);

    /// <summary>Captures an actual reading, validating it against the meter's history.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="meterId">The meter.</param>
    /// <param name="date">The reading date (after the last reading).</param>
    /// <param name="reading">The register value.</param>
    /// <param name="tamperSuspected">Whether the reader suspects tampering.</param>
    /// <param name="thresholds">The high / low thresholds (the default when null).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The outcome (rejected readings are recorded but leave the meter unchanged), or why it was refused.</returns>
    Task<Result<ReadingOutcome>> CaptureAsync(long companyId, long meterId, DateOnly date, decimal reading, bool tamperSuspected, ReadingThresholds? thresholds = null, CancellationToken ct = default);

    /// <summary>Estimates a reading from the meter's historical daily consumption.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="meterId">The meter.</param>
    /// <param name="date">The date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The estimate, or why there is none.</returns>
    Task<Result<ReadingOutcome>> EstimateAsync(long companyId, long meterId, DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// Replaces a meter: takes the old meter's final reading, removes it, installs the new one at its start reading,
    /// carries over any units credit and moves the property services to it, in one unit.
    /// </summary>
    /// <param name="companyId">The council.</param>
    /// <param name="oldMeterId">The meter taken out.</param>
    /// <param name="date">The replacement date.</param>
    /// <param name="finalReading">The old meter's final reading.</param>
    /// <param name="newSerial">The new meter's serial.</param>
    /// <param name="newDigits">The new meter's dials.</param>
    /// <param name="newMultiplier">The new meter's multiplier.</param>
    /// <param name="newStartReading">The new meter's register value.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The replacement, or why it was refused.</returns>
    Task<Result<MeterReplacement>> ReplaceAsync(long companyId, long oldMeterId, DateOnly date, decimal finalReading, string newSerial, int newDigits, decimal newMultiplier, decimal newStartReading, CancellationToken ct = default);

    /// <summary>Flagged readings over a period, oldest first.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="start">The first day.</param>
    /// <param name="end">The last day.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The exceptions.</returns>
    Task<IReadOnlyList<ReadingException>> ExceptionsAsync(long companyId, DateOnly start, DateOnly end, CancellationToken ct = default);

    /// <summary>A meter's readings and consumption, oldest first.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="meterId">The meter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The history.</returns>
    Task<IReadOnlyList<ConsumptionLine>> HistoryAsync(long companyId, long meterId, CancellationToken ct = default);
}
