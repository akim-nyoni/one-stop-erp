using FluentAssertions;
using One4All.Application.Communications;
using Xunit;

namespace One4All.UnitTests.Communications;

public class TemplateRendererTests
{
    [Fact]
    public void Fields_are_merged_ignoring_case()
    {
        RenderedMessage message = TemplateRenderer.Render("Statement for {{Account}}", "Dear {{name}}, you owe {{ Amount }}.",
            new Dictionary<string, string> { ["account"] = "C-001", ["Name"] = "Mrs Moyo", ["amount"] = "1,250.00" });
        (message.Subject, message.Body, message.MissingFields.Count).Should().Be(("Statement for C-001", "Dear Mrs Moyo, you owe 1,250.00.", 0));
    }

    [Fact]
    public void Missing_fields_are_reported_once_and_left_visible()
    {
        RenderedMessage message = TemplateRenderer.Render("{{Account}}", "{{Name}} {{Account}} {{Due}}", new Dictionary<string, string> { ["Name"] = "A" });
        message.MissingFields.Should().Equal("Account", "Due");
        message.Body.Should().Be("A {{Account}} {{Due}}");
    }

    [Fact]
    public void Literal_braces_and_non_fields_pass_through()
    {
        RenderedMessage message = TemplateRenderer.Render(string.Empty, "Use {{{{Name}} for a field; {{ not a field }} and {single}.", new Dictionary<string, string>());
        (message.Body, message.MissingFields.Count).Should().Be(("Use {{Name}} for a field; {{ not a field }} and {single}.", 0));
    }

    [Fact]
    public void The_fields_a_template_uses_are_listed_in_order()
    {
        TemplateRenderer.Fields("{{Name}} owes {{Amount}} by {{Due}}; {{name}} again").Should().Equal("Name", "Amount", "Due");
    }
}
