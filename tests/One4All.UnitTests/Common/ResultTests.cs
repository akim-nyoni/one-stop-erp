using FluentAssertions;
using One4All.Core.Common;
using Xunit;

namespace One4All.UnitTests.Common;

public class ResultTests
{
    [Fact]
    public void Success_has_no_error()
    {
        Result result = Result.Success();
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Error.Should().Be(Error.None);
    }

    [Fact]
    public void Failure_carries_error()
    {
        Error error = Error.Validation("X.Bad", "bad input");
        Result result = Result.Failure(error);
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void Generic_success_exposes_value()
    {
        Result<int> result = Result.Success(42);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Accessing_value_of_failure_throws()
    {
        Result<int> result = Result.Failure<int>(Error.NotFound("X.Missing", "not found"));
        Action act = () => _ = result.Value;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Implicit_conversions_work()
    {
        Result<string> fromValue = "hello";
        fromValue.IsSuccess.Should().BeTrue();
        fromValue.Value.Should().Be("hello");

        Result<string> fromError = Error.Conflict("X.Dup", "duplicate");
        fromError.IsFailure.Should().BeTrue();
        fromError.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public void TryGetValue_pattern()
    {
        Result.Success("ok").TryGetValue(out string? value).Should().BeTrue();
        value.Should().Be("ok");

        Result.Failure<string>(Error.Rule("R", "r"))
            .TryGetValue(out string? none).Should().BeFalse();
        none.Should().BeNull();
    }

    [Fact]
    public void Inconsistent_construction_is_rejected()
    {
        Action success = () => Result.Failure(Error.None);
        success.Should().Throw<ArgumentException>();
    }
}
