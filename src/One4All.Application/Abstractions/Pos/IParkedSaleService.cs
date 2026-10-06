using One4All.Core.Common;

namespace One4All.Application.Abstractions.Pos;

/// <summary>A parked sale as the till lists it.</summary>
/// <param name="Id">The parked sale.</param>
/// <param name="Tag">The name, table or customer tag.</param>
/// <param name="TerminalId">The till it was parked at.</param>
/// <param name="Lines">The number of lines.</param>
/// <param name="Total">The total so far.</param>
/// <param name="ParkedAt">When.</param>
public sealed record ParkedSaleSummary(long Id, string Tag, long TerminalId, int Lines, decimal Total, DateTimeOffset ParkedAt);

/// <summary>
/// Recall, park and hold (brief §4.1): any number of sales held under a name / table / customer tag, listed, resumed at any till of the
/// company with their lines intact, or discarded with a reason — each step audited with who and when.
/// </summary>
public interface IParkedSaleService
{
    /// <summary>Parks a sale's lines (<c>Pos.ParkInvalid</c> without a tag or lines).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="terminalId">The till.</param>
    /// <param name="tag">The name, table or customer tag.</param>
    /// <param name="userId">Who parks it.</param>
    /// <param name="lines">The lines so far.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The parked sale id.</returns>
    Task<Result<long>> ParkAsync(long companyId, long terminalId, string tag, long userId, IReadOnlyList<PosSaleLineRequest> lines, CancellationToken ct = default);

    /// <summary>The sales parked and not yet resumed or discarded, oldest first.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The list.</returns>
    Task<IReadOnlyList<ParkedSaleSummary>> ParkedAsync(long companyId, CancellationToken ct = default);

    /// <summary>Resumes a parked sale at a till, returning its lines; once only (<c>Pos.ParkedClosed</c>).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="parkedSaleId">The parked sale.</param>
    /// <param name="terminalId">The till resuming it.</param>
    /// <param name="userId">Who.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The lines.</returns>
    Task<Result<IReadOnlyList<PosSaleLineRequest>>> ResumeAsync(long companyId, long parkedSaleId, long terminalId, long userId, CancellationToken ct = default);

    /// <summary>Discards a parked sale with a reason.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="parkedSaleId">The parked sale.</param>
    /// <param name="userId">Who.</param>
    /// <param name="reason">Why.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> DiscardAsync(long companyId, long parkedSaleId, long userId, string reason, CancellationToken ct = default);
}
