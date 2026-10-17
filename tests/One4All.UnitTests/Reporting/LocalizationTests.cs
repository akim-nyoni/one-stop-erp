using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
using One4All.Application.Localization;
using One4All.Core.Common;
using One4All.Reporting.Engine;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>
/// Covers localisation: the text catalog (Portuguese shipped, a regional culture falling back to its language, anything else to English),
/// every shell menu string translated, company terminology (whole words, plurals, capitals), and reports with label translations, the
/// company's terms, locale number formats and right-to-left.
/// </summary>
public sealed partial class LocalizationTests
{
    [Fact]
    public void The_catalog_translates_and_falls_back()
    {
        TextCatalog.Cultures.Should().Contain("pt");
        TextCatalog.For("pt-MZ")["Account Enquiry…"].Should().Be("Consulta de conta…");
        TextCatalog.For("pt")["Not translated anywhere"].Should().Be("Not translated anywhere");
        TextCatalog.For("xx-NOPE")["Account Enquiry…"].Should().Be("Account Enquiry…");
        TextCatalog.For(null)["Exit"].Should().Be("Exit");
        TextCatalog.For("ar-SA").RightToLeft.Should().BeTrue();
        TextCatalog.For("en", new Dictionary<string, string> { ["Stand"] = "Erf" })["Housing - Waiting List, Allocation Runs, Offers, Stands…"].Should().EndWith("Offers, Erfs…");
    }

    [Fact]
    public void Every_shell_menu_string_has_a_portuguese_translation()
    {
        string shell = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "One4All.Desktop", "ShellWindow.xaml"));
        List<string> texts = [.. LocText().Matches(shell).Select(m => m.Groups[1].Value.Replace("&amp;", "&", StringComparison.Ordinal))];
        texts.Should().HaveCountGreaterThan(15);
        TextCatalog.Missing("pt", texts).Should().BeEmpty("translate new menu entries in src/One4All.Application/Localization/Strings/strings.pt.json");
    }

    [Theory]
    [InlineData("Stand register", "Erf register")]
    [InlineData("Allocate stands to applicants", "Allocate erfs to applicants")]
    [InlineData("STAND", "Erf")]
    [InlineData("Standard cost", "Standard cost")]
    [InlineData("Stand-holder", "Erf-holder")]
    public void Terminology_replaces_whole_words(string text, string expected)
        => Terminology.Apply(text, new Dictionary<string, string> { ["Stand"] = "Erf" }).Should().Be(expected);

    [Fact]
    public void Reports_follow_the_locale_translations_and_terms()
    {
        ReportDefinition definition = new()
        {
            Code = "L", Name = "L",
            LabelTranslations = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase) { ["pt"] = new() { ["title"] = "Registo de talhões" } },
            Bands =
            [
                new ReportBand { Type = BandType.ReportHeader, Height = 20, Elements = [new ReportElement { Kind = ElementKind.Label, Name = "title", Text = "Stand register" }] },
                new ReportBand { Type = BandType.Detail, Height = 16, Elements =
                [
                    new ReportElement { Kind = ElementKind.Field, Name = "amount", FieldName = "Amount", Format = "N2" },
                    new ReportElement { Kind = ElementKind.Field, Name = "date", FieldName = "Date" },
                ] },
            ],
        };
        List<IReadOnlyDictionary<string, object?>> rows = [new Dictionary<string, object?> { ["Amount"] = 1234.5m, ["Date"] = new DateOnly(2026, 3, 31) }];
        Dictionary<string, string> terms = new() { ["Stand"] = "Erf" };

        RenderModel invariant = new ReportEngine().Build(new ReportInput { Definition = definition, Rows = rows, Terminology = terms });
        invariant.ReportHeader!.Cells[0].Text.Should().Be("Erf register");
        invariant.Body[0].Cells[0].Text.Should().Be("1,234.50");

        RenderModel portuguese = new ReportEngine().Build(new ReportInput { Definition = definition, Rows = rows, Terminology = terms, Culture = CultureInfo.GetCultureInfo("pt-MZ") });
        portuguese.ReportHeader!.Cells[0].Text.Should().Be("Registo de talhões", "pt-MZ falls back to the pt translation");
        portuguese.Body[0].Cells[0].Text.Should().Be(1234.5m.ToString("N2", CultureInfo.GetCultureInfo("pt-MZ")));
        portuguese.Body[0].Cells[1].Text.Should().Be(new DateOnly(2026, 3, 31).ToString("d", CultureInfo.GetCultureInfo("pt-MZ")));
        portuguese.RightToLeft.Should().BeFalse();

        RenderModel arabic = new ReportEngine().Build(new ReportInput { Definition = definition, Rows = rows, Culture = CultureInfo.GetCultureInfo("ar-SA") });
        arabic.RightToLeft.Should().BeTrue();
        System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(arabic)).Should().Contain("<html dir=\"rtl\">");
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "One4All.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [GeneratedRegex(@"\{loc:T '([^']*)'\}")]
    private static partial Regex LocText();
}
