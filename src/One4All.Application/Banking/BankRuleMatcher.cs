using One4All.Core.Banking;

namespace One4All.Application.Banking;

/// <summary>The parts of a bank rule the matcher needs.</summary>
/// <param name="Id">The rule.</param>
/// <param name="BankAccountId">The bank account it is limited to, or null for all.</param>
/// <param name="DescriptionContains">The phrase.</param>
/// <param name="Direction">The direction filter.</param>
/// <param name="MinAmount">Optional lower bound on the absolute amount.</param>
/// <param name="MaxAmount">Optional upper bound on the absolute amount.</param>
/// <param name="Priority">Lower is tried first.</param>
public sealed record BankRuleSpec(long Id, long? BankAccountId, string DescriptionContains, BankRuleDirection Direction, decimal? MinAmount, decimal? MaxAmount, int Priority);

/// <summary>
/// Chooses the bank rule (if any) for a statement line: the description must contain the rule's phrase
/// (case-insensitive), the direction and absolute-amount range must fit, and the rule must be for this bank
/// account or all of them. The lowest priority wins, then a bank-specific rule over a general one, then the
/// longest phrase (the most specific), then the lowest id. Pure.
/// </summary>
public static class BankRuleMatcher
{
    /// <summary>Finds the rule for a line.</summary>
    /// <param name="rules">The active rules.</param>
    /// <param name="bankAccountId">The line's bank account.</param>
    /// <param name="description">The line's description.</param>
    /// <param name="amount">The line's signed amount.</param>
    /// <returns>The chosen rule, or null.</returns>
    public static BankRuleSpec? Match(IEnumerable<BankRuleSpec> rules, long bankAccountId, string description, decimal amount)
    {
        if (amount == 0m || string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        decimal size = Math.Abs(amount);
        return rules
            .Where(r => r.DescriptionContains.Length > 0 && description.Contains(r.DescriptionContains, StringComparison.OrdinalIgnoreCase))
            .Where(r => r.BankAccountId is null || r.BankAccountId == bankAccountId)
            .Where(r => r.Direction switch
            {
                BankRuleDirection.MoneyIn => amount > 0m,
                BankRuleDirection.MoneyOut => amount < 0m,
                _ => true,
            })
            .Where(r => (r.MinAmount is null || size >= r.MinAmount) && (r.MaxAmount is null || size <= r.MaxAmount))
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.BankAccountId is null ? 1 : 0)
            .ThenByDescending(r => r.DescriptionContains.Length)
            .ThenBy(r => r.Id)
            .FirstOrDefault();
    }
}
