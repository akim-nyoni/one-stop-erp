using One4All.Application.Trading;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A housing expected-credit-loss assessment request.</summary>
public sealed class HousingEclRequest
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The assessment date.</summary>
    public DateOnly AsAtDate { get; init; }

    /// <summary>The provision matrix for instalment-sale debtors (applied per scheme).</summary>
    public EclMatrix InstalmentMatrix { get; init; } = EclMatrix.Default;

    /// <summary>The provision matrix for council tenants.</summary>
    public EclMatrix RentalMatrix { get; init; } = EclMatrix.Default;

    /// <summary>The housing allowance (contra-receivable) account.</summary>
    public string AllowanceAccountCode { get; init; } = string.Empty;

    /// <summary>The impairment expense account (needed to post).</summary>
    public string ExpenseAccountCode { get; init; } = string.Empty;
}

/// <summary>The provision for one housing portfolio.</summary>
/// <param name="Portfolio">The portfolio: a scheme, or council rentals.</param>
/// <param name="Bands">The exposure and provision per arrears band.</param>
public sealed record HousingEclPortfolio(string Portfolio, IReadOnlyList<EclBandResult> Bands)
{
    /// <summary>The portfolio's exposure.</summary>
    public decimal Exposure => Bands.Sum(b => b.Exposure);

    /// <summary>The portfolio's provision.</summary>
    public decimal Provision => Bands.Sum(b => b.Provision);
}

/// <summary>A housing expected-credit-loss assessment.</summary>
/// <param name="AsAtDate">The date.</param>
/// <param name="Portfolios">The provision per portfolio.</param>
/// <param name="BookedAllowance">The allowance already booked.</param>
/// <param name="JournalNumber">The adjusting journal, when posted and there was an adjustment.</param>
public sealed record HousingEclReport(DateOnly AsAtDate, IReadOnlyList<HousingEclPortfolio> Portfolios, decimal BookedAllowance, string? JournalNumber)
{
    /// <summary>The allowance the assessment calls for.</summary>
    public decimal TargetAllowance => Portfolios.Sum(p => p.Provision);

    /// <summary>The movement needed: positive to raise, negative to release.</summary>
    public decimal Adjustment => TargetAllowance - BookedAllowance;
}

/// <summary>
/// IFRS 9 expected credit loss for housing debtors (brief §4.10.9: provided for separately, by arrears bucket and by
/// scheme): instalment-sale buyers grouped by the scheme of the stand they bought, and council tenants, each portfolio
/// banded by days past due on its own matrix, with credits set against the oldest debts; the movement on a dedicated
/// housing allowance is posted, so a repeat posts nothing.
/// </summary>
public interface IHousingEclService
{
    /// <summary>Assesses the housing allowance without posting.</summary>
    /// <param name="request">The assessment.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The assessment, or why there is none.</returns>
    Task<Result<HousingEclReport>> AssessAsync(HousingEclRequest request, CancellationToken ct = default);

    /// <summary>Assesses and posts the movement on the housing allowance (Dr impairment / Cr allowance, or the reverse).</summary>
    /// <param name="request">The assessment.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The assessment with the journal, or why it was refused.</returns>
    Task<Result<HousingEclReport>> PostAsync(HousingEclRequest request, CancellationToken ct = default);
}
