using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A covenant on the register.</summary>
/// <param name="CovenantNumber">The covenant.</param>
/// <param name="StandNumber">The stand.</param>
/// <param name="Deadline">The current deadline.</param>
/// <param name="Latest">The latest milestone reached.</param>
/// <param name="Extensions">Extensions granted.</param>
/// <param name="Status">Where it stands.</param>
public sealed record CovenantEntry(string CovenantNumber, string StandNumber, DateOnly Deadline, CovenantMilestone? Latest, int Extensions, CovenantStatus Status);

/// <summary>
/// Development covenants (brief §4.10.5: build within N years or the stand reverts): record the covenant on a stand,
/// its milestones in order, extensions granted for a fee, the breach run that flags overdue covenants and raises the
/// penalty levy once, and the notice of intention to repossess.
/// </summary>
public interface IDevelopmentCovenantService
{
    /// <summary>Records a covenant on a stand held by a customer, with a deadline a number of years out.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="holderCustomerId">The holder.</param>
    /// <param name="startDate">When it starts.</param>
    /// <param name="years">Years allowed to complete construction.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The covenant number, or why it was refused.</returns>
    Task<Result<string>> CreateAsync(long companyId, long standId, long holderCustomerId, DateOnly startDate, int years, CancellationToken ct = default);

    /// <summary>Records the next milestone (in order, never back-dated); completion discharges the covenant.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="covenantNumber">The covenant.</param>
    /// <param name="milestone">The milestone.</param>
    /// <param name="date">When.</param>
    /// <param name="reference">The plan, licence or inspection reference.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> RecordMilestoneAsync(long companyId, string covenantNumber, CovenantMilestone milestone, DateOnly date, string? reference, CancellationToken ct = default);

    /// <summary>Grants an extension, invoicing its fee to the holder; a breached covenant reopens. One unit.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="covenantNumber">The covenant.</param>
    /// <param name="months">Months added to the deadline.</param>
    /// <param name="date">When granted.</param>
    /// <param name="fee">The extension fee.</param>
    /// <param name="feeAccountCode">The revenue account for the fee (needed when there is one).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The new deadline, or why it was refused.</returns>
    Task<Result<DateOnly>> GrantExtensionAsync(long companyId, string covenantNumber, int months, DateOnly date, decimal fee, string? feeAccountCode, CancellationToken ct = default);

    /// <summary>Flags every open covenant past its deadline as breached and invoices the penalty levy once. One unit.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="asAtDate">The run date.</param>
    /// <param name="penaltyLevy">The penalty levy per breach.</param>
    /// <param name="levyAccountCode">The revenue account for the levy.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The covenants found in breach, or why the run was refused.</returns>
    Task<Result<IReadOnlyList<string>>> RunBreachesAsync(long companyId, DateOnly asAtDate, decimal penaltyLevy, string levyAccountCode, CancellationToken ct = default);

    /// <summary>Issues the notice of intention to repossess on a breached covenant.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="covenantNumber">The covenant.</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> IssueNoticeAsync(long companyId, string covenantNumber, DateOnly date, CancellationToken ct = default);

    /// <summary>The covenant register.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The covenants, by number.</returns>
    Task<IReadOnlyList<CovenantEntry>> RegisterAsync(long companyId, CancellationToken ct = default);
}
