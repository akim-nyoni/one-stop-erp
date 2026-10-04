using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A compensation claim on the register.</summary>
/// <param name="ClaimNumber">The claim.</param>
/// <param name="ParcelNumber">The parcel acquired.</param>
/// <param name="Claimant">The claimant.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Provision">The provision held.</param>
/// <param name="SettledAmount">What was paid, once settled.</param>
public sealed record CompensationEntry(string ClaimNumber, string ParcelNumber, string Claimant, CompensationStatus Status, decimal Provision, decimal? SettledAmount);

/// <summary>
/// Compensation on compulsory acquisition (brief §4.10.9 / IAS 37: a provision once the obligation is probable and
/// measurable, not on payment): lodge a claim against a land parcel; provide for it at the best estimate (capitalised
/// into land under development) and remeasure; settle it (any difference adjusts the land cost); or withdraw it,
/// releasing the provision.
/// </summary>
public interface ICompensationService
{
    /// <summary>Lodges a claim; nothing is provided until it is probable and measurable.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="landParcelId">The parcel acquired.</param>
    /// <param name="claimant">The claimant.</param>
    /// <param name="lodgedOn">When.</param>
    /// <param name="landCostAccountCode">The land (work in progress) account.</param>
    /// <param name="provisionAccountCode">The provision account.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The claim number, or why it was refused.</returns>
    Task<Result<string>> LodgeAsync(long companyId, long landParcelId, string claimant, DateOnly lodgedOn, string landCostAccountCode, string provisionAccountCode, CancellationToken ct = default);

    /// <summary>Provides for a claim at its best estimate, or remeasures it, posting only the movement. One unit.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="claimNumber">The claim.</param>
    /// <param name="bestEstimate">The best estimate of what will be paid.</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The provision held, or why it was refused.</returns>
    Task<Result<decimal>> ProvideAsync(long companyId, string claimNumber, decimal bestEstimate, DateOnly date, CancellationToken ct = default);

    /// <summary>Settles a claim: the provision is used, the payment made, any difference adjusts the land cost. One unit.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="claimNumber">The claim.</param>
    /// <param name="paid">What was paid.</param>
    /// <param name="bankAccountCode">The bank account paid from.</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> SettleAsync(long companyId, string claimNumber, decimal paid, string bankAccountCode, DateOnly date, CancellationToken ct = default);

    /// <summary>Withdraws a claim, releasing any provision against the land cost. One unit.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="claimNumber">The claim.</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> WithdrawAsync(long companyId, string claimNumber, DateOnly date, CancellationToken ct = default);

    /// <summary>The claims register.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The claims, by number.</returns>
    Task<IReadOnlyList<CompensationEntry>> RegisterAsync(long companyId, CancellationToken ct = default);
}
