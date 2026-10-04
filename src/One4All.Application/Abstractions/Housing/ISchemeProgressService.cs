using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>An engineer's progress certificate for one bulk service in a scheme.</summary>
public sealed record ServicingCertificate
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The scheme code.</summary>
    public string SchemeCode { get; init; } = string.Empty;

    /// <summary>The service.</summary>
    public BulkService Service { get; init; }

    /// <summary>The certificate date.</summary>
    public DateOnly Date { get; init; }

    /// <summary>Percentage complete, 0–100.</summary>
    public decimal PercentComplete { get; init; }

    /// <summary>How many stands the service now reaches.</summary>
    public int StandsReached { get; init; }

    /// <summary>The certificate reference.</summary>
    public string Reference { get; init; } = string.Empty;
}

/// <summary>
/// Scheme delivery progress (brief §14 H9, H12, H13): survey stages recorded for stands (a surveyor pegs or lodges a whole
/// layout at once, so it takes many stands, all or none); servicing certificates per bulk service (progress never goes
/// backwards); and valuations of undivided land for the IAS 2 net-realisable-value test.
/// </summary>
public interface ISchemeProgressService
{
    /// <summary>Records that stands reached a survey stage.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="standNumbers">The stands.</param>
    /// <param name="stage">The stage reached.</param>
    /// <param name="date">When.</param>
    /// <param name="reference">The surveyor's job or diagram number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>How many stands were recorded.</returns>
    Task<Result<int>> RecordSurveyAsync(long companyId, IReadOnlyList<string> standNumbers, SurveyStage stage, DateOnly date, string reference, CancellationToken ct = default);

    /// <summary>Records a servicing progress certificate.</summary>
    /// <param name="certificate">The certificate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Its id.</returns>
    Task<Result<long>> RecordServicingAsync(ServicingCertificate certificate, CancellationToken ct = default);

    /// <summary>Records a valuation of a land parcel.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="parcelNumber">The parcel.</param>
    /// <param name="date">The valuation date.</param>
    /// <param name="value">The open-market value.</param>
    /// <param name="valuer">Who valued it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Its id.</returns>
    Task<Result<long>> RecordValuationAsync(long companyId, string parcelNumber, DateOnly date, decimal value, string valuer, CancellationToken ct = default);
}
