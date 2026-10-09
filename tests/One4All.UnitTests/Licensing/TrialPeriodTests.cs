using FluentAssertions;
using One4All.Licensing.Core;
using One4All.Licensing.Generator.Vendor;
using Xunit;

namespace One4All.UnitTests.Licensing;

/// <summary>Covers trials counted from first activation and the code a licence certificate's QR carries.</summary>
public sealed class TrialPeriodTests
{
    [Fact]
    public void A_trial_runs_its_days_from_first_activation_then_turns_read_only()
    {
        LicensePayload trial = new() { LicenceType = LicenceType.Trial, Limits = { [TrialPeriod.DaysLimit] = 30 } };
        DateTimeOffset installed = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
        TrialPeriod.EndsOn(trial, installed).Should().Be(new DateOnly(2026, 3, 30));
        TrialPeriod.EndsOn(new LicensePayload { LicenceType = LicenceType.Subscription, Limits = { [TrialPeriod.DaysLimit] = 30 } }, installed).Should().BeNull("only a trial licence");

        LicenseValidationResult Valid(DateTimeOffset now) => LicenseValidationResult.ForPayload(LicenseStatus.Valid, trial, "Valid.", new FixedClock(now));
        LicenseValidationResult during = TrialPeriod.Apply(Valid(new DateTimeOffset(2026, 3, 30, 12, 0, 0, TimeSpan.Zero)), installed, new FixedClock(new DateTimeOffset(2026, 3, 30, 12, 0, 0, TimeSpan.Zero)));
        (during.IsUsable, during.Message).Should().Be((true, "Trial: 1 day(s) left (to 2026-03-30)."));
        LicenseValidationResult after = TrialPeriod.Apply(Valid(new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero)), installed, new FixedClock(new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero)));
        (after.IsUsable, after.IsReadOnly).Should().Be((false, true));
    }

    [Fact]
    public void The_certificate_code_names_the_licence_and_fingerprints_its_token()
    {
        IssuedLicence licence = new() { LicenceId = "L-1", CustomerId = "C-9", ValidTo = new DateOnly(2027, 1, 31), Token = "token-text" };
        string code = LicenceDesk.VerificationCode(licence);
        code.Should().StartWith("O4A-LIC|L-1|C-9|2027-01-31|").And.HaveLength("O4A-LIC|L-1|C-9|2027-01-31|".Length + 16);
        LicenceDesk.VerificationCode(new IssuedLicence { LicenceId = "L-1", CustomerId = "C-9", ValidTo = new DateOnly(2027, 1, 31), Token = "other" }).Should().NotBe(code);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
