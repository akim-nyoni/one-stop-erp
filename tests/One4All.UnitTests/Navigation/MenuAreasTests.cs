using FluentAssertions;
using One4All.Application.Navigation;
using Xunit;

namespace One4All.UnitTests.Navigation;

/// <summary>Covers placing the console's actions in Sage-style menus: set-up in Maintenance, documents in Transactions, look-ups in Enquiries, period-end and housekeeping in Utilities.</summary>
public sealed class MenuAreasTests
{
    [Theory]
    [InlineData("Add a customer", MenuArea.Maintenance)]
    [InlineData("Set a salary scale notch", MenuArea.Maintenance)]
    [InlineData("Council service types", MenuArea.Maintenance)]
    [InlineData("Reset a password", MenuArea.Maintenance)]
    [InlineData("Customer invoice or credit note", MenuArea.Transactions)]
    [InlineData("Receive goods", MenuArea.Transactions)]
    [InlineData("Add an approver to a document", MenuArea.Transactions)]
    [InlineData("Create gift card stock", MenuArea.Transactions)]
    [InlineData("Customer statement", MenuArea.Enquiries)]
    [InlineData("Find an item", MenuArea.Enquiries)]
    [InlineData("Cash-flow forecast", MenuArea.Enquiries)]
    [InlineData("Year-end", MenuArea.Utilities)]
    [InlineData("Revalue foreign currency balances", MenuArea.Utilities)]
    [InlineData("Import master data or opening balances", MenuArea.Utilities)]
    [InlineData("Run the annual increment", MenuArea.Utilities)]
    public void Actions_land_in_their_menu(string title, MenuArea area) => MenuAreas.Classify(title).Should().Be(area);
}
