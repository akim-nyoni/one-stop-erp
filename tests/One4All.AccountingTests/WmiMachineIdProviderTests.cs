using FluentAssertions;
using One4All.Infrastructure.Licensing;
using One4All.Licensing.Core;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Exercises the real <see cref="WmiMachineIdProvider"/> on the host running the suite. WMI values are
/// hardware-specific, so the assertions cover only invariants that hold on any machine: it never throws,
/// it yields distinctive components, those components are stable across calls, and they hash into a
/// parseable fingerprint. On Windows it must enrich beyond the portable baseline.
/// </summary>
public sealed class WmiMachineIdProviderTests
{
    private readonly WmiMachineIdProvider _provider = new();

    [Fact]
    public void GetComponents_never_throws_and_always_includes_the_machine_baseline()
    {
        IReadOnlyList<string> components = _provider.GetComponents();

        components.Should().NotBeEmpty();
        components.Should().Contain(c => c.StartsWith("machine:", StringComparison.Ordinal));
        components.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c));
    }

    [Fact]
    public void GetComponents_is_stable_across_calls()
    {
        // The hardware identity does not change between two immediate calls.
        _provider.GetComponents().Should().Equal(_provider.GetComponents());
    }

    [Fact]
    public void Components_hash_into_a_parseable_fingerprint()
    {
        MachineFingerprint fingerprint = MachineFingerprint.FromComponents(_provider.GetComponents());
        string serialized = fingerprint.ToString();

        serialized.Should().StartWith("sha256:");
        // Round-trips and fuzzily matches itself (a machine always matches its own licence binding).
        fingerprint.FuzzyMatches(MachineFingerprint.Parse(serialized)).Should().BeTrue();
    }

    [Fact]
    public void On_windows_the_provider_enriches_beyond_the_portable_baseline()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // At least one hardware component (system UUID / board / bios / os serial / mac / volume) is present.
        string[] hardwareLabels = ["sys-uuid:", "board:", "bios:", "os:", "mac:", "vol:"];
        _provider.GetComponents()
            .Should().Contain(c => hardwareLabels.Any(l => c.StartsWith(l, StringComparison.Ordinal)));
    }
}
