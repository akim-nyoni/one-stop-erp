using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Accounting;

/// <summary>One account on the restated trial balance, debit positive.</summary>
/// <param name="AccountCode">The account.</param>
/// <param name="Name">Its name.</param>
/// <param name="Type">Its type.</param>
/// <param name="Monetary">Whether it is monetary, and so not restated.</param>
/// <param name="Historical">The historical-cost balance.</param>
/// <param name="Restated">The balance in closing purchasing power.</param>
public sealed record RestatedTrialBalanceRow(string AccountCode, string Name, AccountType Type, bool Monetary, decimal Historical, decimal Restated)
{
    /// <summary>The restatement adjustment.</summary>
    public decimal Adjustment => Restated - Historical;
}

/// <summary>The trial balance restated for hyperinflation alongside historical cost (IAS 29).</summary>
/// <param name="AsAtDate">The reporting date.</param>
/// <param name="IndexCode">The price index used.</param>
/// <param name="ClosingIndex">The index for the reporting month.</param>
/// <param name="Rows">The accounts with a balance, by code.</param>
public sealed record RestatedTrialBalance(DateOnly AsAtDate, string IndexCode, decimal ClosingIndex, IReadOnlyList<RestatedTrialBalanceRow> Rows)
{
    /// <summary>
    /// The gain (positive) or loss (negative) on the net monetary position: the amount that balances the restated trial
    /// balance, recognised in profit or loss.
    /// </summary>
    public decimal MonetaryGainLoss => Rows.Sum(r => r.Restated);
}

/// <summary>
/// The optional IAS 29 hyperinflation module (brief §3): keeps a monthly price index and restates the trial balance at a
/// date. Non-monetary balances are restated from the month each amount was recorded; income and expenses from within the
/// current financial year; monetary balances stay as they are, and the difference is the monetary gain or loss.
/// </summary>
public interface IHyperinflationService
{
    /// <summary>Records or corrects a month's index value.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="indexCode">The index, such as CPI.</param>
    /// <param name="month">Any date in the month.</param>
    /// <param name="value">The index value; positive.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> SetIndexAsync(long companyId, string indexCode, DateOnly month, decimal value, CancellationToken ct = default);

    /// <summary>Restates the trial balance at a date.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The reporting date.</param>
    /// <param name="indexCode">The index to restate by.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The restated trial balance, or the months whose index is missing.</returns>
    Task<Result<RestatedTrialBalance>> RestatedTrialBalanceAsync(long companyId, DateOnly asAtDate, string indexCode = "CPI", CancellationToken ct = default);
}
