using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>An encumbrance to register against a parcel.</summary>
public sealed record EncumbranceRegistration
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The parcel number.</summary>
    public string ParcelNumber { get; init; } = string.Empty;

    /// <summary>What it is.</summary>
    public EncumbranceType Type { get; init; }

    /// <summary>The registration reference.</summary>
    public string Reference { get; init; } = string.Empty;

    /// <summary>Who holds the right.</summary>
    public string InFavourOf { get; init; } = string.Empty;

    /// <summary>The particulars.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>When it was registered.</summary>
    public DateOnly Date { get; init; }
}

/// <summary>A land dispute to open.</summary>
public sealed record LandDisputeLodgement
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The parcel number.</summary>
    public string ParcelNumber { get; init; } = string.Empty;

    /// <summary>The stand number, when the dispute is over one stand on the parcel.</summary>
    public string? StandNumber { get; init; }

    /// <summary>What it is about.</summary>
    public LandDisputeType Type { get; init; }

    /// <summary>Who raised it.</summary>
    public string Claimant { get; init; } = string.Empty;

    /// <summary>The particulars.</summary>
    public string Particulars { get; init; } = string.Empty;

    /// <summary>When.</summary>
    public DateOnly Date { get; init; }
}

/// <summary>A disposal or exchange of land.</summary>
public sealed record LandDisposalRequest
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The parcel number.</summary>
    public string ParcelNumber { get; init; } = string.Empty;

    /// <summary>How.</summary>
    public LandDisposalType Type { get; init; }

    /// <summary>To whom.</summary>
    public string Counterparty { get; init; } = string.Empty;

    /// <summary>The council resolution authorising it.</summary>
    public string ResolutionReference { get; init; } = string.Empty;

    /// <summary>When.</summary>
    public DateOnly Date { get; init; }

    /// <summary>The area disposed of (all of the parcel's remaining area when zero).</summary>
    public decimal AreaSquareMetres { get; init; }

    /// <summary>The proceeds, compensation or the fair value of the parcel received in exchange (zero for a donation).</summary>
    public decimal Consideration { get; init; }

    /// <summary>The land-bank account the parcel's cost sits in (and the exchange parcel goes to).</summary>
    public string LandAccountCode { get; init; } = string.Empty;

    /// <summary>The bank or receivable account for sale proceeds or compensation.</summary>
    public string ProceedsAccountCode { get; init; } = string.Empty;

    /// <summary>The gain / loss on disposal account.</summary>
    public string GainLossAccountCode { get; init; } = string.Empty;

    /// <summary>For an exchange: the number of the parcel received.</summary>
    public string? ExchangeParcelNumber { get; init; }

    /// <summary>For an exchange: its description.</summary>
    public string? ExchangeParcelDescription { get; init; }

    /// <summary>For an exchange: its area.</summary>
    public decimal ExchangeParcelArea { get; init; }
}

/// <summary>A recorded disposal.</summary>
/// <param name="DisposalNumber">Its number.</param>
/// <param name="JournalNumber">The journal.</param>
/// <param name="CostDerecognised">The land-bank cost taken out.</param>
/// <param name="GainOrLoss">Consideration less cost.</param>
/// <param name="ExchangeParcelId">The parcel received in exchange.</param>
public sealed record LandDisposalResult(string DisposalNumber, string JournalNumber, decimal CostDerecognised, decimal GainOrLoss, long? ExchangeParcelId);

/// <summary>
/// The land register beyond acquisition (brief §14 H6–H8): encumbrances (servitudes, caveats, bonds) registered and lifted;
/// land disputes and encroachments taken through their stages to resolution; and disposals and exchanges of undivided
/// land under a council resolution, posted. A caveat in force or an open ownership dispute stops a disposal; a caveat on
/// a parcel or an open dispute over a stand blocks the stand from allocation, sale or transfer.
/// </summary>
public interface ILandRegisterService
{
    /// <summary>Registers an encumbrance.</summary>
    /// <param name="registration">The encumbrance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Its id.</returns>
    Task<Result<long>> RegisterEncumbranceAsync(EncumbranceRegistration registration, CancellationToken ct = default);

    /// <summary>Lifts an encumbrance.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="encumbranceId">The encumbrance.</param>
    /// <param name="date">When.</param>
    /// <param name="reference">The cancelling document.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why not.</returns>
    Task<Result> LiftEncumbranceAsync(long companyId, long encumbranceId, DateOnly date, string reference, CancellationToken ct = default);

    /// <summary>Opens a land dispute under the next LD number.</summary>
    /// <param name="lodgement">The dispute.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Its number.</returns>
    Task<Result<string>> OpenDisputeAsync(LandDisputeLodgement lodgement, CancellationToken ct = default);

    /// <summary>Moves a dispute to a later stage (Resolved closes it, with the note as its outcome).</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="disputeNumber">The dispute.</param>
    /// <param name="stage">The new stage.</param>
    /// <param name="date">When.</param>
    /// <param name="note">What happened.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why not.</returns>
    Task<Result> AdvanceDisputeAsync(long companyId, string disputeNumber, LandDisputeStage stage, DateOnly date, string note, CancellationToken ct = default);

    /// <summary>Disposes of or exchanges land, posting the derecognition, the consideration and the gain or loss.</summary>
    /// <param name="request">The disposal.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The disposal.</returns>
    Task<Result<LandDisposalResult>> DisposeAsync(LandDisposalRequest request, CancellationToken ct = default);
}
