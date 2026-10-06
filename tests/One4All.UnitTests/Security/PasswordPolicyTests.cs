using FluentAssertions;
using One4All.Application.Security;
using Xunit;

namespace One4All.UnitTests.Security;

/// <summary>Covers the password rules.</summary>
public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData("short1", "bob", "at least 8")]
    [InlineData("lettersonly", "bob", "letters and digits")]
    [InlineData("1234567890", "bob", "letters and digits")]
    [InlineData("xBob12345", "bob", "user name")]
    public void Weak_passwords_are_refused(string password, string user, string reason) => PasswordPolicy.Problem(password, user).Should().Contain(reason);

    [Fact]
    public void A_long_mixed_password_passes() => PasswordPolicy.Problem("Spring2026a", "bob").Should().BeNull();
}
