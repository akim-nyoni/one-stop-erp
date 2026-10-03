using One4All.Core.Vouchers;

namespace One4All.Application.Vouchers;

/// <summary>Promotion discount arithmetic. Pure and unit-tested.</summary>
public static class PromoPricing
{
    /// <summary>The discount a promotion gives on a spend, never more than the spend itself.</summary>
    /// <param name="type">Percentage or amount off.</param>
    /// <param name="value">The percentage or amount.</param>
    /// <param name="spend">The spend.</param>
    /// <returns>The discount, to the cent.</returns>
    public static decimal Discount(PromoDiscountType type, decimal value, decimal spend)
    {
        if (spend <= 0m || value <= 0m)
        {
            return 0m;
        }

        decimal discount = type == PromoDiscountType.Percent
            ? decimal.Round(spend * Math.Min(value, 100m) / 100m, 2, MidpointRounding.AwayFromZero)
            : value;
        return Math.Min(discount, spend);
    }
}
