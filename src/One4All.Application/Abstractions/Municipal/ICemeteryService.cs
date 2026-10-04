using One4All.Core.Common;
using One4All.Core.Municipal;

namespace One4All.Application.Abstractions.Municipal;

/// <summary>A request to record an interment.</summary>
public sealed class IntermentRequest
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The plot reference (cemetery-section-number).</summary>
    public string PlotReference { get; init; } = string.Empty;

    /// <summary>The deceased.</summary>
    public string DeceasedName { get; init; } = string.Empty;

    /// <summary>The date of death.</summary>
    public DateOnly DateOfDeath { get; init; }

    /// <summary>The burial date.</summary>
    public DateOnly IntermentDate { get; init; }

    /// <summary>The burial fee invoiced to the plot holder, if any.</summary>
    public decimal IntermentFee { get; init; }

    /// <summary>The revenue account the burial fee is credited to (needed when there is a fee).</summary>
    public string? FeeAccountCode { get; init; }
}

/// <summary>A plot on the register.</summary>
/// <param name="Reference">The plot reference.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Price">The selling price.</param>
/// <param name="Capacity">How many interments it can hold.</param>
/// <param name="Interred">How many it holds.</param>
/// <param name="HolderCode">The holder, once sold.</param>
public sealed record PlotEntry(string Reference, CemeteryPlotStatus Status, decimal Price, int Capacity, int Interred, string? HolderCode);

/// <summary>An entry in the burial register.</summary>
/// <param name="IntermentNumber">The register number.</param>
/// <param name="PlotReference">The plot.</param>
/// <param name="DeceasedName">The deceased.</param>
/// <param name="DateOfDeath">The date of death.</param>
/// <param name="IntermentDate">The burial date.</param>
public sealed record BurialEntry(string IntermentNumber, string PlotReference, string DeceasedName, DateOnly DateOfDeath, DateOnly IntermentDate);

/// <summary>
/// The cemetery register (brief §4.9 cemetery revenue): plots by cemetery, section and number; plot sales invoiced to
/// the holder; interments recorded against sold plots up to their capacity, with any burial fee invoiced; the plots
/// available and the burial register.
/// </summary>
public interface ICemeteryService
{
    /// <summary>Adds plots to a cemetery section; none may already exist.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="cemetery">The cemetery.</param>
    /// <param name="section">The section.</param>
    /// <param name="plotNumbers">The plot numbers.</param>
    /// <param name="price">The selling price of each.</param>
    /// <param name="capacity">How many interments each can hold.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number added, or why they were refused.</returns>
    Task<Result<int>> AddPlotsAsync(long companyId, string cemetery, string section, IReadOnlyList<string> plotNumbers, decimal price, int capacity, CancellationToken ct = default);

    /// <summary>Sells an available plot to a holder and invoices its price, in one unit.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="plotReference">The plot.</param>
    /// <param name="holderCustomerId">The holder.</param>
    /// <param name="date">The sale date.</param>
    /// <param name="revenueAccountCode">The revenue account the sale is credited to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The sale invoice number, or why it was refused.</returns>
    Task<Result<string>> SellAsync(long companyId, string plotReference, long holderCustomerId, DateOnly date, string revenueAccountCode, CancellationToken ct = default);

    /// <summary>Records an interment in a sold plot with room, invoicing any burial fee to the holder, in one unit.</summary>
    /// <param name="request">The interment.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The burial register number, or why it was refused.</returns>
    Task<Result<string>> RecordIntermentAsync(IntermentRequest request, CancellationToken ct = default);

    /// <summary>The plots in a cemetery, optionally only those in one state, in reference order.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="cemetery">The cemetery.</param>
    /// <param name="status">Only plots in this state, or null for all.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The plots.</returns>
    Task<IReadOnlyList<PlotEntry>> PlotsAsync(long companyId, string cemetery, CemeteryPlotStatus? status, CancellationToken ct = default);

    /// <summary>The burial register for a period, in date order.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="start">The first day.</param>
    /// <param name="end">The last day.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The burials.</returns>
    Task<IReadOnlyList<BurialEntry>> BurialRegisterAsync(long companyId, DateOnly start, DateOnly end, CancellationToken ct = default);
}
