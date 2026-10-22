using One4All.Core.Common;
using One4All.Core.Municipal;

namespace One4All.Application.Abstractions.Municipal;

/// <summary>A stand's value on an imported general valuation roll.</summary>
/// <param name="StandNumber">The stand number.</param>
/// <param name="Value">The value.</param>
public sealed record RollEntry(string StandNumber, decimal Value);

/// <summary>The rates adjustment raised when a value is backdated over bills already raised.</summary>
/// <param name="BillsAdjusted">The rates bills the change reached.</param>
/// <param name="Amount">The total invoiced (positive) or credited (negative).</param>
/// <param name="DocumentNumber">The invoice or credit note, when there was a difference.</param>
public sealed record ValuationAdjustment(int BillsAdjusted, decimal Amount, string? DocumentNumber)
{
    /// <summary>Gets whether the credit note is held in the credit-note approval workflow rather than posted.</summary>
    public bool HeldForApproval { get; init; }
}

/// <summary>A line of a property's valuation history.</summary>
/// <param name="EffectiveFrom">The first day it applies.</param>
/// <param name="Value">The value.</param>
/// <param name="Kind">Where it came from.</param>
/// <param name="Reference">The roll name, supplementary number or objection number.</param>
public sealed record ValuationEntry(DateOnly EffectiveFrom, decimal Value, ValuationKind Kind, string Reference);

/// <summary>A line of the objections register.</summary>
/// <param name="ObjectionNumber">The objection number.</param>
/// <param name="StandNumber">The stand.</param>
/// <param name="LodgedOn">When it was lodged.</param>
/// <param name="ValueObjectedTo">The value objected to.</param>
/// <param name="ClaimedValue">The value claimed.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="DecidedValue">The revised value, when upheld.</param>
public sealed record ObjectionEntry(string ObjectionNumber, string StandNumber, DateOnly LodgedOn, decimal ValueObjectedTo, decimal ClaimedValue, ObjectionStatus Status, decimal? DecidedValue);

/// <summary>
/// The valuation roll (brief §4.9): dated valuation history per property, general roll imports, supplementary
/// valuations and the objections register. Rates bills use the value in force on the bill date; a supplementary value
/// or an objection outcome backdated over bills already raised is invoiced or credited as one adjustment.
/// </summary>
public interface IValuationRollService
{
    /// <summary>Imports a general valuation roll, all or nothing; the values apply from the roll's date.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="rollName">The roll's name (for example "GV2027").</param>
    /// <param name="effectiveFrom">The date the roll takes effect.</param>
    /// <param name="entries">The stands and values.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of values recorded, or why the roll was refused.</returns>
    Task<Result<int>> ImportRollAsync(long companyId, string rollName, DateOnly effectiveFrom, IReadOnlyList<RollEntry> entries, CancellationToken ct = default);

    /// <summary>
    /// Records a supplementary valuation and adjusts rates bills raised since it took effect, in one unit. It may take effect no earlier than
    /// the council's back-dating limit (<see cref="Core.System.Company.ValuationBackdateMonths"/>; by default the start of the financial
    /// year). A credit above <see cref="Core.System.Company.ValuationCreditApprovalThreshold"/> goes through the credit-note approval workflow.
    /// </summary>
    /// <param name="companyId">The council.</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="effectiveFrom">The first day the new value applies.</param>
    /// <param name="value">The new value.</param>
    /// <param name="reason">Why (required).</param>
    /// <param name="adjustmentDate">The date of the adjustment; bills up to it are adjusted.</param>
    /// <param name="userId">The user recording it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The adjustment, or why it was refused.</returns>
    Task<Result<ValuationAdjustment>> RecordSupplementaryAsync(long companyId, long propertyId, DateOnly effectiveFrom, decimal value, string reason, DateOnly adjustmentDate, long userId,
        CancellationToken ct = default);

    /// <summary>Lodges an objection to the value in force on the lodging date; one open objection per property.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="lodgedOn">When it was lodged.</param>
    /// <param name="claimedValue">The value the objector claims.</param>
    /// <param name="grounds">The grounds (required).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The objection number, or why it was refused.</returns>
    Task<Result<string>> LodgeObjectionAsync(long companyId, long propertyId, DateOnly lodgedOn, decimal claimedValue, string grounds, CancellationToken ct = default);

    /// <summary>
    /// Decides an objection: with a revised value it is upheld, the value replaces the one objected to from the same date
    /// and rates bills since then are adjusted (a credit above the council's threshold through the credit-note approval workflow); without one
    /// it is dismissed. One unit.
    /// </summary>
    /// <param name="companyId">The council.</param>
    /// <param name="objectionNumber">The objection.</param>
    /// <param name="decidedOn">The decision date; bills up to it are adjusted.</param>
    /// <param name="revisedValue">The revised value, or null to dismiss.</param>
    /// <param name="notes">The decision's reasons (required).</param>
    /// <param name="userId">The user recording the decision.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The adjustment (nothing when dismissed), or why it was refused.</returns>
    Task<Result<ValuationAdjustment>> DecideObjectionAsync(long companyId, string objectionNumber, DateOnly decidedOn, decimal? revisedValue, string notes, long userId, CancellationToken ct = default);

    /// <summary>
    /// Sets the council's valuation controls: how many months back a supplementary valuation may take effect (null: the start of the financial
    /// year), and the largest rates credit a valuation change posts at once (null: every credit goes through the credit-note approval workflow).
    /// </summary>
    /// <param name="companyId">The council.</param>
    /// <param name="backdateMonths">The back-dating limit in months, or null.</param>
    /// <param name="creditApprovalThreshold">The credit approval threshold, or null.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> SetPolicyAsync(long companyId, int? backdateMonths, decimal? creditApprovalThreshold, CancellationToken ct = default);

    /// <summary>A property's valuation history in the order it was recorded.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The history.</returns>
    Task<IReadOnlyList<ValuationEntry>> HistoryAsync(long companyId, long propertyId, CancellationToken ct = default);

    /// <summary>The objections register, optionally for one status, oldest first.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="status">The status, or null for all.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The objections.</returns>
    Task<IReadOnlyList<ObjectionEntry>> ObjectionsAsync(long companyId, ObjectionStatus? status, CancellationToken ct = default);
}
