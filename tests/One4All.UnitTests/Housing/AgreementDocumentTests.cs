using FluentAssertions;
using One4All.Application.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class AgreementDocumentTests
{
    private static readonly AgreementContent Content = new("AGR-1", 1, "InstalmentSale", "City Council", "J. Moyo", "S-12", 13000m, "1,000 down, 12 monthly instalments", "Build within 2 years");

    [Fact]
    public void The_document_carries_every_term()
    {
        string document = AgreementDocument.Render(Content);
        document.Should().Contain("AGREEMENT AGR-1 (version 1)").And.Contain("INSTALMENTSALE").And.Contain("City Council").And.Contain("J. Moyo")
            .And.Contain("stand S-12").And.Contain("13,000.00").And.Contain("12 monthly instalments").And.Contain("Build within 2 years");
    }

    [Fact]
    public void The_same_content_always_renders_and_hashes_the_same()
    {
        string first = AgreementDocument.Render(Content);
        AgreementDocument.Render(Content).Should().Be(first);
        AgreementDocument.Hash(first).Should().HaveLength(64).And.Be(AgreementDocument.Hash(AgreementDocument.Render(Content)));
    }

    [Fact]
    public void Any_change_changes_the_hash()
    {
        string hash = AgreementDocument.Hash(AgreementDocument.Render(Content));
        AgreementDocument.Hash(AgreementDocument.Render(Content with { Price = 13001m })).Should().NotBe(hash);
        AgreementDocument.Hash(AgreementDocument.Render(Content with { Version = 2 })).Should().NotBe(hash);
    }

    [Fact]
    public void Optional_parts_are_left_out_when_empty()
    {
        string document = AgreementDocument.Render(Content with { StandNumber = null, SpecialConditions = null });
        document.Should().NotContain("Property:").And.NotContain("Special conditions:");
    }
}
