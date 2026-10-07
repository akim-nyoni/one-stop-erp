using One4All.Application.Banking;

namespace One4All.Application.Abstractions.Banking;

/// <summary>
/// The full cash-flow forecast (brief §4.4): bank balances, then week by week what receivables and payables fall due, saved payment runs on
/// their payment dates, open sales and purchase orders on delivery plus terms, standing orders and payroll — with the weeks cash would fall
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
}
