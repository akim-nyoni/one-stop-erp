using Microsoft.EntityFrameworkCore;
using One4All.Application.Payroll;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Payroll;

namespace One4All.Infrastructure.Payroll;

/// <summary>
/// The multi-currency rules the pay run, the final pay and the payroll reports share: split-currency tax, and how a net pay goes out by currency
/// (as the settlement and the bank file pay it).
/// </summary>
internal static class PayrollCurrencies
{
    /// <summary>
    /// Split-currency tax: each foreign currency's taxable earnings with a table of its own are taxed on it at the pay date's rate, its bands scaled
    /// to the employee's pay period as the base table's are; everything else bears its share of the base table's tax on the whole remuneration.
    /// The credits come off the total, each currency's share reduced in proportion so the shares add up to the tax deducted.
    /// </summary>
    /// <param name="tableCode">The tax table code the element uses (null: any).</param>
    /// <param name="tables">The company's tax tables, bands included.</param>
    /// <param name="portions">The taxable earnings (in base) by the currency they are paid in ("" the base currency).</param>
    /// <param name="rates">The pay date's rate (base per unit) of each foreign currency.</param>
    /// <param name="baseTax">The base table's tax on the whole taxable pay, before credits.</param>
    /// <param name="taxable">The whole taxable pay.</param>
    /// <param name="payDate">The pay date.</param>
    /// <param name="scale">The period's share of a month (12 / periods a year) the monthly bands are scaled by.</param>
    /// <param name="credits">The period's tax credits.</param>
    /// <returns>The tax (rounded to the cent) and what each currency bears (in base), or the missing rate.</returns>
    public static Result<(decimal Tax, Dictionary<string, decimal> Shares)> SplitTax(string? tableCode, IEnumerable<TaxTable> tables, IReadOnlyDictionary<string, decimal> portions,
        IReadOnlyDictionary<string, decimal> rates, decimal baseTax, decimal taxable, DateOnly payDate, decimal scale, decimal credits)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(portions);
        ArgumentNullException.ThrowIfNull(rates);
        List<TaxTable> all = [.. tables];
        decimal tax = 0m;
        Dictionary<string, decimal> shares = new(StringComparer.Ordinal);
        foreach ((string currency, decimal earnings) in portions)
        {
            TaxTable? own = currency.Length == 0 ? null : all
                .Where(t => t.Currency?.Value == currency && t.EffectiveFrom <= payDate && (tableCode == null || string.Equals(t.Code, tableCode, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(t => t.EffectiveFrom).LastOrDefault();
            if (own is null)
            {
                decimal share = taxable == 0m ? 0m : baseTax * earnings / taxable;
                shares[currency] = shares.GetValueOrDefault(currency) + share;
                tax += share;
                continue;
            }

            if (!rates.TryGetValue(currency, out decimal rate) || rate <= 0m)
            {
                return Error.Validation("Payroll.NoRate", $"No {currency} rate on {payDate:yyyy-MM-dd} to tax the earnings paid in it.");
            }

            decimal ownTax = PayeTable.Tax(own.Bands.Select(b => new PayeBand(b.From * scale, b.To * scale, b.RatePercent)), earnings / rate) * rate;
            shares[currency] = shares.GetValueOrDefault(currency) + ownTax;
            tax += ownTax;
        }

        decimal credited = TaxCredits.Apply(tax, credits);
        decimal factor = tax == 0m ? 0m : credited / tax;
        foreach (string currency in shares.Keys.ToList())
        {
            shares[currency] *= factor;
        }

        return (decimal.Round(credited, 2, MidpointRounding.ToEven), shares);
    }

    /// <summary>
    /// The base amount of a net pay paid in each foreign currency: earnings paid in another currency go out in it, never more than the net
    /// (deductions come off the base part first); then the employee's splits paid in another currency take their share of what is left, in order.
    /// </summary>
    /// <param name="foreignEarnings">The earnings (in base) by the foreign currency they are paid in.</param>
    /// <param name="netPay">The net pay (in base).</param>
    /// <param name="splits">The employee's payment splits, in order.</param>
    /// <param name="baseCurrency">The base currency.</param>
    /// <returns>The base amount paid in each foreign currency (nil amounts left out).</returns>
    public static Dictionary<CurrencyCode, decimal> ForeignNet(IEnumerable<KeyValuePair<CurrencyCode, decimal>> foreignEarnings, decimal netPay, IEnumerable<EmployeePaymentSplit> splits,
        CurrencyCode baseCurrency)
    {
        ArgumentNullException.ThrowIfNull(foreignEarnings);
        Dictionary<CurrencyCode, decimal> foreign = [];
        decimal left = netPay;
        foreach ((CurrencyCode currency, decimal earned) in foreignEarnings.Where(e => e.Key != baseCurrency).OrderBy(e => e.Key.Value, StringComparer.Ordinal))
        {
            decimal portion = Math.Max(Math.Min(earned, left), 0m);
            foreign[currency] = foreign.GetValueOrDefault(currency) + portion;
            left -= portion;
        }

        foreach ((EmployeePaymentSplit split, decimal amount) in EmployeePaymentSplit.Allocate(splits, netPay, left).Where(x => x.Amount > 0m && x.Split.Currency is { } c && c != baseCurrency).ToList())
        {
            foreign[split.Currency!.Value] = foreign.GetValueOrDefault(split.Currency!.Value) + amount;
        }

        return foreign.Where(f => f.Value > 0m).ToDictionary(f => f.Key, f => f.Value);
    }

    /// <summary>
    /// The account an exchange difference on pay paid in another currency goes to: a gain (the currency weakened) to the FX gain control account, a
    /// loss to the FX loss one.
    /// </summary>
    /// <param name="db">The database context.</param>
    /// <param name="companyId">The company.</param>
    /// <param name="exchange">The difference in base: positive a gain, negative a loss.</param>
    /// <param name="what">What was paid, for the message ("Net pay").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The account (null when there is no difference), or that the control account is missing.</returns>
    public static async Task<Result<string?>> ExchangeAccountAsync(Persistence.One4AllDbContext db, long companyId, decimal exchange, string what, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (exchange == 0m)
        {
            return Result<string?>.Success(null);
        }

        ControlAccountType fxType = exchange > 0m ? ControlAccountType.FxGain : ControlAccountType.FxLoss;
        string? account = await db.Accounts.Where(a => a.CompanyId == companyId && a.ControlAccountType == fxType).OrderBy(a => a.AccountCode).Select(a => a.AccountCode)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return account is null
            ? Error.Rule("Payroll.NoFxAccount", $"{what} paid at a different rate from the pay date's gives an exchange {(exchange > 0m ? "gain" : "loss")} of {Math.Abs(exchange):N2}; "
                + $"set up an {fxType} control account first.")
            : Result<string?>.Success(account);
    }
}
