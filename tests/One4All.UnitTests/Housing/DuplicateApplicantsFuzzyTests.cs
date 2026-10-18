using FluentAssertions;
using One4All.Application.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

/// <summary>
/// Thirteenth audit #23: duplicate applicants matched more closely - IDs one character apart meet even with nothing else in common, names that
/// sound the same or give an initial are close, a swapped day and month is caught, and a short name allows only one edit.
/// </summary>
public sealed class DuplicateApplicantsFuzzyTests
{
    private static PersonRecord Applicant(string reference, string name, string? id, DateOnly? born = null) => new(DuplicateApplicants.WaitingListSource, reference, name, id, born);

    [Fact]
    public void Ids_one_character_apart_meet_without_a_shared_name_key_or_birth_date()
    {
        IReadOnlyList<DuplicateMatch> matches = DuplicateApplicants.Find([Applicant("WL-1", "John Moyo", "63-123456A07"), Applicant("WL-2", "Jon Moyo", "63-123456A08")]);
        matches.Should().ContainSingle().Which.Reason.Should().Be("Similar name with ID numbers one character apart");
    }

    [Fact]
    public void Sound_alike_names_and_a_swapped_birth_date_are_caught()
    {
        DuplicateApplicants.SoundKey("Tendai Dube").Should().Be(DuplicateApplicants.SoundKey("Tendayi Dhube"));
        IReadOnlyList<DuplicateMatch> matches = DuplicateApplicants.Find(
            [Applicant("WL-1", "Tendai Dube", "44-999999D44", new DateOnly(1990, 3, 4)), Applicant("WL-2", "Tendayi Dhube", "12-345678E90", new DateOnly(1990, 4, 3))]);
        matches.Should().ContainSingle().Which.Reason.Should().Be("Similar name with the date of birth's day and month swapped");
    }

    [Fact]
    public void An_initial_matches_the_word_it_stands_for()
    {
        DuplicateApplicants.NamesClose("T. Dube", "Tendai Dube").Should().BeTrue();
        DuplicateApplicants.NamesClose("Dube T", "Tendai Dube").Should().BeTrue();
        DuplicateApplicants.NamesClose("R Dube", "Tendai Dube").Should().BeFalse();
        DuplicateApplicants.NamesClose("T M", "Tendai Moyo").Should().BeFalse("initials alone are not enough");
        DuplicateApplicants.Find([Applicant("WL-1", "T Dube", "44-999999D44", new DateOnly(1990, 3, 4)), Applicant("WL-2", "Tendai Dube", "55-111111F11", new DateOnly(1990, 3, 4))])
            .Should().ContainSingle().Which.Reason.Should().Be("Similar name and same date of birth under different ID numbers");
    }

    [Fact]
    public void A_short_name_allows_one_edit()
    {
        DuplicateApplicants.NamesClose("Bob Ali", "Rod Ali").Should().BeFalse("two edits in a six-letter name is a different name");
        DuplicateApplicants.NamesClose("Ann Ziba", "Anne Ziba").Should().BeTrue();
    }
}
