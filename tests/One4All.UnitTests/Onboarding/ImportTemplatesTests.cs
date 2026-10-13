using FluentAssertions;
using One4All.Application.Onboarding;
using Xunit;

namespace One4All.UnitTests.Onboarding;

/// <summary>Covers the import templates (every import has a header and an example) and CSV writing (quoting and invariant formats).</summary>
public sealed class ImportTemplatesTests
{
    [Fact]
    public void Every_import_has_a_template_and_values_are_quoted_when_needed()
    {
        ImportTemplates.Kinds.Should().HaveCount(19);
        ImportTemplates.For("customers").Should().StartWith("code,name,currency,credit_limit\r\nCUST001,");
        ImportTemplates.For("Nonsense").Should().BeNull();
        foreach (string kind in ImportTemplates.Kinds)
        {
            string[] lines = ImportTemplates.For(kind)!.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            lines.Should().HaveCount(2, kind);
        }

        ImportTemplates.Csv("code,name,amount,date,flag", [["A1", "Smith, John", 1234.5m, new DateOnly(2026, 3, 1), true], ["A2", "Say \"hi\"", null, null, false]])
            .Should().Be("code,name,amount,date,flag\r\nA1,\"Smith, John\",1234.5,2026-03-01,true\r\nA2,\"Say \"\"hi\"\"\",,,false\r\n");
    }
}
