using One4All.Core.Payroll;

namespace One4All.Application.Payroll;

/// <summary>A tax credit as the payroll applies it.</summary>
/// <param name="Code">The credit.</param>
/// <param name="Basis">How it is worked out.</param>
/// <param name="Amount">The monthly amount (fixed).</param>
/// <param name="Percent">The percentage (of an element).</param>
/// <param name="ElementCode">The element it is a percentage of.</param>
public sealed record TaxCreditRule(string Code, TaxCreditBasis Basis, decimal Amount, decimal Percent, string? ElementCode);

/// <summary>Tax credits against the tax the table works out (brief §4.15.3): non-refundable, so they never take the tax below nil. Pure.</summary>
public static class TaxCredits
{
    /// <summary>Whether an age-limited credit applies on the pay date.</summary>
    /// <param name="minimumAge">The age it applies from (null: any).</param>
    /// <param name="dateOfBirth">The employee's date of birth.</param>
    /// <param name="payDate">The pay date.</param>
    /// <returns>True when it applies.</returns>
    public static bool Eligible(int? minimumAge, DateOnly? dateOfBirth, DateOnly payDate)
    {
        if (minimumAge is not int age)
        {
            return true;
        }

        if (dateOfBirth is not DateOnly born)
        {
            return false;
        }

        int years = payDate.Year - born.Year - (payDate < born.AddYears(payDate.Year - born.Year) ? 1 : 0);
        return years >= age;
    }

    /// <summary>The credits for a pay period.</summary>
    /// <param name="rules">The employee's credits.</param>
    /// <param name="element">The period's amount of a pay element.</param>
    /// <param name="periodScale">The period's share of a month (12 / periods a year).</param>
    /// <returns>The total credit.</returns>
    public static decimal Total(IEnumerable<TaxCreditRule> rules, Func<string, decimal> element, decimal periodScale)
        => decimal.Round(rules.Sum(r => r.Basis == TaxCreditBasis.Fixed ? r.Amount * periodScale : r.ElementCode is null ? 0m : Math.Abs(element(r.ElementCode)) * r.Percent / 100m),
            2, MidpointRounding.AwayFromZero);

    /// <summary>The tax after credits, never below nil.</summary>
    /// <param name="tax">The tax the table works out.</param>
    /// <param name="credits">The credits.</param>
    /// <returns>The tax deducted.</returns>
    public static decimal Apply(decimal tax, decimal credits) => Math.Max(0m, tax - Math.Max(0m, credits));
}
