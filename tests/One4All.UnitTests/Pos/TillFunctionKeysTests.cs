using FluentAssertions;
using One4All.Application.Pos;
using One4All.Core.Pos;
using Xunit;

namespace One4All.UnitTests.Pos;

/// <summary>Covers till function keys: profile defaults, a terminal's own map over them, and refusing unknown keys or actions.</summary>
public sealed class TillFunctionKeysTests
{
    [Fact]
    public void A_terminals_keys_override_its_profiles_defaults()
    {
        TillFunctionKeys.Defaults(TerminalProfile.Bar)["F10"].Should().Be("AddTip");
        TillFunctionKeys.Defaults(TerminalProfile.Butchery)["F10"].Should().Be("Weigh");
        TillFunctionKeys.Defaults(TerminalProfile.General)["F2"].Should().Be("TenderCash");

        IReadOnlyDictionary<string, string> keys = TillFunctionKeys.For(TerminalProfile.Bar, "f2=tendercard; F1=VoidSale");
        keys["F2"].Should().Be("TenderCard");
        keys["F1"].Should().Be("VoidSale");
        keys["F10"].Should().Be("AddTip");
        TillFunctionKeys.Describe(keys).Should().StartWith("F1 VoidSale  F2 TenderCard");

        TillFunctionKeys.Parse("F13=Pay").Error.Code.Should().Be("Pos.FunctionKey");
        TillFunctionKeys.Parse("F3=Teleport").Error.Code.Should().Be("Pos.FunctionKeyAction");
        TillFunctionKeys.Parse(null).Value.Should().BeEmpty();
        TillFunctionKeys.For(TerminalProfile.General, "garbage")["F5"].Should().Be("Pay", "an unreadable map keeps the defaults");
    }
}
