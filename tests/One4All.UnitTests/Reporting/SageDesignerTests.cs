using FluentAssertions;
using One4All.Reporting.Design;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>The designer's Sage-style chrome: toolbox, Dynamic Help tasks, property categories and descriptions, numbered section captions, grouping and report info.</summary>
public sealed class SageDesignerTests
{
    [Fact]
    public void Toolbox_help_and_property_catalogue_are_complete()
    {
        DesignerCatalogue.Tools[0].Key.Should().Be("Pointer");
        DesignerCatalogue.Tools.Select(t => t.Key).Should().OnlyHaveUniqueItems();
        DesignerCatalogue.Tools.Select(t => t.Caption).Should().Contain(["Add Text", "Draw Line", "Draw Box", "Add Image / Logo", "Add Data Field", "Add Barcode", "Add Expression", "Add Subreport"]);
        DesignerCatalogue.Tools.Where(t => t.Key != "Pointer").Should().OnlyContain(t => t.Kind != null);
        DesignerCatalogue.HelpTasks.Select(t => t.Title).Should().Contain(["Set the report name or description", "Add text or data field", "Add an image or logo", "Add a line or box",
            "Restrict the information included in the report", "Sort information in the report", "Set up email"]);
        foreach (string property in ReportDesignSession.EditableProperties)
        {
            DesignerCatalogue.Categories.Should().Contain(DesignerCatalogue.Category(property), property);
            DesignerCatalogue.Describe(property).Should().NotBe($"The {property} of the element.", $"{property} needs a description for the pane under the property grid");
        }

        DesignerCatalogue.Category("Name").Should().Be("Design");
        DesignerCatalogue.Category("X").Should().Be("Layout");
        DesignerCatalogue.Category("Bold").Should().Be("Appearance");
        DesignerCatalogue.Category("SuppressWhen").Should().Be("Behaviour");
        DesignerCatalogue.Category("FieldName").Should().Be("Data");
    }

    [Fact]
    public void Sections_are_numbered_and_group_sections_are_named_after_their_group()
    {
        ReportDesignSession s = new(new ReportDefinition { Code = "T", Name = "Test" }, grid: 1);
        s.AddBand(BandType.PageHeader);
        s.AddBand(BandType.Detail);
        s.AddBand(BandType.PageFooter);
        ReportBand header = s.AddGroupBand(BandType.GroupHeader, "CustomerCode");
        ReportBand footer = s.AddGroupBand(BandType.GroupFooter, "CustomerCode");
        s.Definition.Groups.Select(g => g.Expression).Should().Equal("CustomerCode");
        (header.GroupLevel, footer.GroupLevel).Should().Be((1, 1));
        header.Elements.Single().FieldName.Should().Be("CustomerCode", "a group header starts with the group's field");
        s.AddGroupBand(BandType.GroupHeader, "CustomerCode").Should().BeSameAs(header, "one header per group");
        s.Definition.Bands.Select((b, i) => DesignerCatalogue.BandCaption(i + 1, b, s.Definition))
            .Should().Equal("1. Page Header", "2. CustomerCode header", "3. Details", "4. CustomerCode footer", "5. Page Footer");

        s.SetGroups("Region; CustomerCode desc").Should().BeNull();
        s.GroupsText().Should().Be("Region; CustomerCode desc");
        s.Definition.Bands.Single(b => b.Type == BandType.GroupHeader).GroupLevel.Should().Be(1, "the band keeps its level; Region is now group 1");
        s.SetGroups(string.Empty).Should().BeNull();
        s.Definition.Bands.Should().NotContain(b => b.Type == BandType.GroupHeader || b.Type == BandType.GroupFooter, "group sections go with their groups");
        s.SetGroups("desc").Should().NotBeNull();
        s.Undo();
        s.Definition.Groups.Should().HaveCount(2);
    }

    [Fact]
    public void Report_info_and_toolbar_formatting_are_single_undo_steps()
    {
        ReportDesignSession s = new(new ReportDefinition { Code = "T", Name = "Test" }, grid: 1);
        s.SetReportInfo(" ", null).Should().NotBeNull();
        s.SetReportInfo("Aged debtors", "Who owes what").Should().BeNull();
        (s.Definition.Name, s.Definition.Description).Should().Be(("Aged debtors", "Who owes what"));
        s.Undo();
        s.Definition.Name.Should().Be("Test");

        string a = s.AddElement(BandType.Detail, ElementKind.Label, 0, 0, "A");
        string b = s.AddElement(BandType.Detail, ElementKind.Label, 50, 0, "B");
        s.SetPropertyOnAll([a, b], nameof(ReportElement.Bold), "true").Should().BeNull();
        s.Find(a).Element!.Bold.Should().BeTrue();
        s.Find(b).Element!.Bold.Should().BeTrue();
        s.Undo();
        (s.Find(a).Element!.Bold, s.Find(b).Element!.Bold).Should().Be((false, false), "one undo step for the whole toolbar action");
        s.Find(b).Element.Should().NotBeNull("the undo did not go back past the add");
        s.SetPropertyOnAll([], nameof(ReportElement.Bold), "true").Should().NotBeNull();
        s.SetPropertyOnAll([a], nameof(ReportElement.FontSize), "-3").Should().NotBeNull();
    }
}
