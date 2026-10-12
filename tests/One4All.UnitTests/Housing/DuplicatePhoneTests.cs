using FluentAssertions;
using One4All.Application.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

/// <summary>Covers matching duplicates on the phone number.</summary>
public sealed class DuplicatePhoneTests
{
    [Fact]
    public void The_same_phone_under_two_ids_is_suspicious()
    {
        PersonRecord a = new(DuplicateApplicants.WaitingListSource, "WL1", "Tendai Moyo", "11-111111A11", null, "+263 77 123 4567");
        PersonRecord b = new(DuplicateApplicants.WaitingListSource, "WL2", "Rudo Ncube", "22-222222B22", null, "0771234567");
        PersonRecord c = new(DuplicateApplicants.WaitingListSource, "WL3", "Farai Dube", "33-333333C33", null, "0779999999");
        DuplicateApplicants.Find([a, b, c]).Should().ContainSingle(m => m.Reason == "Same phone number under different ID numbers" && m.First.Reference == "WL1" && m.Second.Reference == "WL2");
    }
}
