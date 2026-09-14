using System.Runtime.InteropServices;

namespace One4All.Licensing.Generator;

/// <summary>
/// Gathers stable machine components for a fingerprint using only cross-platform, dependency-free
/// sources. This is the reference gatherer used by the <c>machineid</c> command; the production ERP
/// host uses a richer Windows provider (motherboard UUID, NIC MAC, volume serial via WMI).
/// </summary>
public static class HostMachineId
{
    /// <summary>Gathers the raw component strings for this machine.</summary>
    /// <returns>The raw components.</returns>
    public static IReadOnlyList<string> GatherComponents()
    {
        List<string> components =
        [
            $"machine:{Environment.MachineName}",
            $"os:{RuntimeInformation.OSDescription}",
            $"arch:{RuntimeInformation.OSArchitecture}",
            $"cpu:{Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")}",
        ];

        string? computerName = Environment.GetEnvironmentVariable("COMPUTERNAME");
        if (!string.IsNullOrWhiteSpace(computerName))
        {
            components.Add($"cn:{computerName}");
        }

        return components;
    }
}
