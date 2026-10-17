using One4All.Application.Banking;

namespace One4All.Application.Abstractions.Banking;

/// <summary>
/// The full cash-flow forecast (brief §4.4): bank balances, then week by week what receivables and payables fall due, saved payment runs on
/// their payment dates, open sales and purchase orders on delivery plus terms, standing orders and payroll - with the weeks cash would fall
/// below a minimum flagged.
/// </summary>
public interface ICashForecastService
{
    /// <summary>Forecasts cash from a date.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="from">The first day (bank balances at the end of the day before).</param>
    /// <param name="weeks">How many weeks (1–52).</param>
    /// <param name="minimumBalance">The minimum cash to hold.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The forecast.</returns>
    Task<CashForecast> ForecastAsync(long companyId, DateOnly from, int weeks, decimal minimumBalance, CancellationToken ct = default);

    /// <summary>Keys a flow into the forecast by hand: once, or monthly until a date (positive in, negative out).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="date">The (first) date.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="description">What it is.</param>
    /// <param name="repeatMonthlyUntil">The last date of a monthly repeat (null: once).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The entry's id.</returns>
    Task<Core.Common.Result<long>> AddEntryAsync(long companyId, DateOnly date, decimal amount, string description, DateOnly? repeatMonthlyUntil = null, CancellationToken ct = default);

    /// <summary>Removes a hand-keyed flow.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="entryId">The entry.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or not found.</returns>
    Task<Core.Common.Result> RemoveEntryAsync(long companyId, long entryId, CancellationToken ct = default);
}
