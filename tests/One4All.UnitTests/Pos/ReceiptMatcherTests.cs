using FluentAssertions;
using One4All.Application.Receipting;
using One4All.Core.Receipting;
using Xunit;

namespace One4All.UnitTests.Pos;

/// <summary>Covers matching an imported payment to one account.</summary>
public sealed class ReceiptMatcherTests
{
    private static readonly Dictionary<string, long> Accounts = new() { ["AC0012"] = 12, ["AC001"] = 1, ["EMP-42"] = 42, ["C1"] = 7 };
    private static readonly Dictionary<string, long> Stands = new() { ["1234-HIGHFIELD"] = 55, ["77"] = 56 };

    private static ReceiptMatch Match(string reference, string name = "", params MatchRule[] rules) => ReceiptMatcher.Match(reference, name, rules, Accounts, Stands);

    [Fact]
    public void An_account_code_typed_with_spaces_or_dashes_is_found_as_a_word()
    {
        Match("PAYMENT AC 00-12 THANKS").Should().Be(new ReceiptMatch(12, MatchedBy.AccountCode));
        Match("ac0012").Should().Be(new ReceiptMatch(12, MatchedBy.AccountCode));
        Match("REF AC001").Should().Be(new ReceiptMatch(1, MatchedBy.AccountCode), "the whole word AC001 is one account even though AC0012 starts with it");
    }

    [Fact]
    public void A_stand_number_matches_its_ratepayer()
    {
        Match("STAND 1234 HIGHFIELD").Should().Be(new ReceiptMatch(55, MatchedBy.StandNumber));
        Match("rates for 77").Should().Be(new ReceiptMatch(56, MatchedBy.StandNumber));
    }

    [Fact]
    public void A_code_anywhere_matches_only_when_it_is_the_only_one()
    {
        Match("XEMP42X").Should().Be(new ReceiptMatch(42, MatchedBy.CodeInReference));
        Match("XAC0012X").Should().Be(new ReceiptMatch(null, MatchedBy.None), "AC001 and AC0012 both appear, so it goes to suspense");
        Match("NO ACCOUNT QUOTED").Should().Be(new ReceiptMatch(null, MatchedBy.None));
    }

    [Fact]
    public void The_council_s_rules_come_first_in_priority_order()
    {
        MatchRule employer = new(ReceiptMatchField.PayerName, ReceiptMatchKind.Contains, "ministry of health", 42, 10);
        MatchRule water = new(ReceiptMatchField.Reference, ReceiptMatchKind.StartsWith, "WTR", 7, 20);
        MatchRule pattern = new(ReceiptMatchField.Reference, ReceiptMatchKind.Pattern, @"^INV\d{4}$", 1, 5);
        Match("AC0012", "MINISTRY OF HEALTH - SALARIES", water, employer).Should().Be(new ReceiptMatch(42, MatchedBy.Rule));
        Match("wtr-9981", "Someone", water, employer).Should().Be(new ReceiptMatch(7, MatchedBy.Rule));
        Match("INV2026", "Someone", water, employer, pattern).Should().Be(new ReceiptMatch(1, MatchedBy.Rule));
        Match("AC0012", "Someone", water, employer).Should().Be(new ReceiptMatch(12, MatchedBy.AccountCode));
    }

    [Fact]
    public void A_rule_needs_a_usable_pattern()
    {
        ReceiptMatcher.Problem(ReceiptMatchKind.Contains, " - ").Should().NotBeNull();
        ReceiptMatcher.Problem(ReceiptMatchKind.Pattern, "([").Should().Contain("not a valid regular expression");
        ReceiptMatcher.Problem(ReceiptMatchKind.Pattern, @"^\d+$").Should().BeNull();
    }
}
