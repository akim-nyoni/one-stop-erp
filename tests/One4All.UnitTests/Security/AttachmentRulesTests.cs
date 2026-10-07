using System.Text;
using FluentAssertions;
using One4All.Application.Attachments;
using Xunit;

namespace One4All.UnitTests.Security;

/// <summary>Covers what may be attached: allowed types, content matching its extension, no executables, the size limit.</summary>
public sealed class AttachmentRulesTests
{
    [Fact]
    public void Real_documents_are_accepted_with_their_media_type()
    {
        AttachmentRules.Check("invoice.PDF", Encoding.ASCII.GetBytes("%PDF-1.7 ...")).Should().Be(("application/pdf", (string?)null));
        AttachmentRules.Check("scan.jpg", [0xFF, 0xD8, 0xFF, 0xE0, 1, 2]).ContentType.Should().Be("image/jpeg");
        AttachmentRules.Check("notes.txt", Encoding.UTF8.GetBytes("hello")).ContentType.Should().Be("text/plain");
    }

    [Theory]
    [InlineData("setup.exe", "MZ")]
    [InlineData("fake.pdf", "MZ...")]
    [InlineData("fake.pdf", "<html>")]
    [InlineData("macro.docm", "PK")]
    [InlineData("empty.pdf", "")]
    public void Wrong_or_dangerous_files_are_refused(string name, string content)
        => AttachmentRules.Check(name, Encoding.ASCII.GetBytes(content)).Problem.Should().NotBeNull();

    [Fact]
    public void Files_over_the_limit_are_refused()
        => AttachmentRules.Check("big.txt", new byte[2048], maxBytes: 1024).Problem.Should().Contain("limit");
}
