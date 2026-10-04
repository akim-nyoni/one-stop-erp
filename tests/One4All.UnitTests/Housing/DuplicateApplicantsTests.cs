using FluentAssertions;
using One4All.Application.Housing;
using One4All.Core.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class DuplicateApplicantsTests
{
    private static readonly DateOnly Born = new(1985, 4, 12);

    private static PersonRecord Applicant(string number, string name, string? id, DateOnly? born = null) => new(DuplicateApplicants.WaitingListSource, number, name, id, born);

    [Fact]
    public void Names_and_ids_compare_loosely()
    {
        DuplicateApplicants.NameKey("Moyo, John").Should().Be(DuplicateApplicants.NameKey("JOHN  MOYO"));
        DuplicateApplicants.NormaliseId("63-123456 A 07").Should().Be("63123456A07");
        DuplicateApplicants.Distance("KITTEN", "SITTING").Should().Be(3);
    }

    [Fact]
    public void Suspicious_pairs_are_found_once_each()
    {
        PersonRecord twiceA = Applicant("WL-1", "John Moyo", "63-123456A07", Born);
        PersonRecord twiceB = Applicant("WL-2", "Moyo John", "63123456 A 07", Born);
        PersonRecord borrowedId = new("Stand holder", "S-9", "Peter Banda", "63-123456A07", null);
        PersonRecord sameBirth = Applicant("WL-3", "Jon Moyo", "70-555555B12", Born);
        PersonRecord typoA = Applicant("WL-4", "Grace Ncube", "08-111111C22");
        PersonRecord typoB = new("Property owner", "P-4", "Grace Ncube", "08-111112C22", null);
        PersonRecord stranger = Applicant("WL-5", "Tendai Dube", "44-999999D44", Born.AddDays(1));

        IReadOnlyList<DuplicateMatch> matches = DuplicateApplicants.Find([twiceA, twiceB, borrowedId, sameBirth, typoA, typoB, stranger]);

        matches.Select(m => (m.First.Reference, m.Second.Reference, m.Reason)).Should().BeEquivalentTo(
        [
            ("WL-1", "WL-2", "Same ID registered twice on the waiting list"),
            ("WL-1", "S-9", "Same ID number under different names"),
            ("WL-2", "S-9", "Same ID number under different names"),
            ("WL-1", "WL-3", "Similar name and same date of birth under different ID numbers"),
            ("WL-2", "WL-3", "Similar name and same date of birth under different ID numbers"),
            ("WL-4", "P-4", "Similar name with ID numbers one character apart"),
        ]);
    }

    [Fact]
    public void A_holder_matching_their_own_application_is_not_suspicious()
    {
        DuplicateApplicants.Reason(Applicant("WL-1", "John Moyo", "63-123456A07"), new PersonRecord("Stand holder", "S-1", "MOYO JOHN", "63123456A07", null)).Should().BeNull();
    }

    [Theory]
    [InlineData(300, HousingCategory.HighDensity)]
    [InlineData(301, HousingCategory.MediumDensity)]
    [InlineData(800, HousingCategory.MediumDensity)]
    [InlineData(801, HousingCategory.LowDensity)]
    public void Stands_supply_a_density_by_size(int size, HousingCategory expected)
    {
        HousingDensity.ForStand(size).Should().Be(expected);
        HousingDensity.ForUnit(RentalUnitType.Flat).Should().Be(HousingCategory.Flats);
    }
}
