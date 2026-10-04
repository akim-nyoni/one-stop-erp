using FluentAssertions;
using One4All.Application.Housing;
using One4All.Core.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class TransferChecklistTests
{
    [Fact]
    public void A_sale_for_value_needs_every_check_including_tax()
    {
        TransferChecklist.Required(TransferType.Sale, 9000m).Should().Equal(
            TransferRequirement.MunicipalClearance, TransferRequirement.HousingAccountClear, TransferRequirement.Identity, TransferRequirement.SpousalConsent,
            TransferRequirement.OriginalDocuments, TransferRequirement.FeesReceipted, TransferRequirement.TaxWithholding);
    }

    [Fact]
    public void A_cession_for_nothing_needs_no_tax_withholding()
    {
        TransferChecklist.Required(TransferType.Cession, 0m).Should().NotContain(TransferRequirement.TaxWithholding);
    }

    [Fact]
    public void An_inheritance_needs_the_estate_documents_but_no_spousal_consent()
    {
        IReadOnlyList<TransferRequirement> items = TransferChecklist.Required(TransferType.Inheritance, 0m);
        items.Should().Contain(TransferRequirement.EstateDocuments).And.NotContain(TransferRequirement.SpousalConsent).And.Contain(TransferRequirement.MunicipalClearance);
    }

    [Theory]
    [InlineData(TransferType.ChangeOfName)]
    [InlineData(TransferType.Correction)]
    public void A_change_that_keeps_the_holder_skips_clearance_and_originals(TransferType type)
    {
        TransferChecklist.Required(type, 0m).Should().Equal(TransferRequirement.Identity, TransferRequirement.FeesReceipted);
    }

    [Fact]
    public void Only_clearance_and_the_housing_account_are_live()
    {
        Enum.GetValues<TransferRequirement>().Where(TransferChecklist.IsLive).Should().Equal(TransferRequirement.MunicipalClearance, TransferRequirement.HousingAccountClear);
    }
}
