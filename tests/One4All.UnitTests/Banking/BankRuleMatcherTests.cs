using FluentAssertions;
using One4All.Application.Banking;
using One4All.Core.Banking;
using Xunit;

namespace One4All.UnitTests.Banking;

/// <summary>Covers choosing a bank rule for a statement line.</summary>
public sealed class BankRuleMatcherTests
{
    private static BankRuleSpec Rule(long id, string phrase, BankRuleDirection direction = BankRuleDirection.Either, long? bank = null, decimal? min = null, decimal? max = null, int priority = 100) =>
        new(id, bank, phrase, direction, min, max, priority);

    [Fact]
    public void Phrase_is_case_insensitive_and_direction_and_range_must_fit()
    {
        BankRuleSpec[] rules = [Rule(1, "service fee", BankRuleDirection.MoneyOut, max: 50m)];

        BankRuleMatcher.Match(rules, 7, "MONTHLY SERVICE FEE", -12.50m)!.Id.Should().Be(1);
        BankRuleMatcher.Match(rules, 7, "Service fee refund", 12.50m).Should().BeNull();   // wrong direction
        BankRuleMatcher.Match(rules, 7, "Service fee", -80m).Should().BeNull();            // above the range
        BankRuleMatcher.Match(rules, 7, "Card purchase", -12.50m).Should().BeNull();       // phrase absent
        BankRuleMatcher.Match(rules, 7, "Service fee", 0m).Should().BeNull();
    }

    [Fact]
    public void Priority_then_bank_specific_then_longest_phrase_wins()
    {
        BankRuleSpec general = Rule(1, "fee");
        BankRuleSpec longer = Rule(2, "card fee");
        BankRuleSpec specificBank = Rule(3, "fee", bank: 7);
        BankRuleSpec otherBank = Rule(4, "card fee", bank: 9);

        BankRuleMatcher.Match([general, longer], 7, "Card fee March", -3m)!.Id.Should().Be(2);           // longer phrase
        BankRuleMatcher.Match([longer, specificBank], 7, "Card fee March", -3m)!.Id.Should().Be(3);      // bank-specific beats general
        BankRuleMatcher.Match([otherBank, general], 7, "Card fee March", -3m)!.Id.Should().Be(1);        // another bank's rule never applies

        BankRuleSpec urgent = Rule(5, "fee", priority: 1);
        BankRuleMatcher.Match([specificBank, urgent], 7, "Card fee March", -3m)!.Id.Should().Be(5);      // priority first
    }
}
