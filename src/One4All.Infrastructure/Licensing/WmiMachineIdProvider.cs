using System.Management;
using System.Runtime.Versioning;
using One4All.Licensing.Core;

namespace One4All.Infrastructure.Licensing;

/// <summary>
/// Gathers stable machine components on Windows via WMI (brief §11.5): system/motherboard UUID, baseboard
/// and BIOS serials, the Windows installation serial, the primary NIC MAC and the system-drive volume
/// serial. Each raw value is later hashed by <see cref="MachineFingerprint"/>, so the values never leave
/// the machine. On non-Windows hosts (or if WMI is unavailable) it degrades to portable environment
/// components so the service graph always has a working provider.
///
/// <para>Every WMI query is wrapped so a missing class, a denied query or a placeholder value can never
/// throw or block startup; obvious OEM placeholders (e.g. "To be filled by O.E.M.") are discarded so a
/// weak value does not dilute the fingerprint. The returned components are order-stable across calls on
/// the same machine, which matters because binding tolerates a minority of components changing.</para>
/// </summary>
public sealed class WmiMachineIdProvider : IMachineIdProvider
{
    // Values BIOS/board vendors leave as fillers - never distinctive, so they are dropped.
    private static readonly HashSet<string> Placeholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "0", "none", "n/a", "na", "default string", "to be filled by o.e.m.",
        "to be filled by oem", "system serial number", "not applicable", "not specified",
        "00000000-0000-0000-0000-000000000000", "ffffffff-ffff-ffff-ffff-ffffffffffff",
    };

    /// <inheritdoc />
    public IReadOnlyList<string> GetComponents()
    {
        List<string> components = [$"machine:{Environment.MachineName}"];

        if (OperatingSystem.IsWindows())
        {
            AddWindowsComponents(components);
        }

        // Fall back to portable components on non-Windows, or when WMI yielded nothing distinctive.
        if (components.Count == 1)
        {
            AddPortableComponents(components);
        }

        return components;
    }

    [SupportedOSPlatform("windows")]
    private void AddWindowsComponents(List<string> components)
    {
        AddScalar(components, "sys-uuid", "Win32_ComputerSystemProduct", "UUID");
        AddScalar(components, "board", "Win32_BaseBoard", "SerialNumber");
        AddScalar(components, "bios", "Win32_BIOS", "SerialNumber");
        AddScalar(components, "os", "Win32_OperatingSystem", "SerialNumber");
        AddPrimaryMac(components);
        AddSystemVolumeSerial(components);
    }

    /// <summary>Reads a single scalar property from the first instance of a WMI class.</summary>
    [SupportedOSPlatform("windows")]
    private void AddScalar(List<string> components, string label, string wmiClass, string property)
    {
        string? value = Query(() =>
        {
            using ManagementObjectSearcher searcher = new($"SELECT {property} FROM {wmiClass}");
            foreach (ManagementBaseObject item in searcher.Get())
            {
                using (item)
                {
                    string? candidate = item[property]?.ToString();
                    if (IsMeaningful(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        });

        Add(components, label, value);
    }

    /// <summary>Adds the lexicographically-smallest physical NIC MAC (deterministic across calls).</summary>
    [SupportedOSPlatform("windows")]
    private void AddPrimaryMac(List<string> components)
    {
        string? mac = Query(() =>
        {
            List<string> macs = [];
            using ManagementObjectSearcher searcher = new(
                "SELECT MACAddress, PhysicalAdapter FROM Win32_NetworkAdapter WHERE MACAddress IS NOT NULL");
            foreach (ManagementBaseObject item in searcher.Get())
            {
                using (item)
                {
                    if (item["PhysicalAdapter"] is bool physical && !physical)
                    {
                        continue;
                    }

                    string? candidate = item["MACAddress"]?.ToString();
                    if (IsMeaningful(candidate))
                    {
                        macs.Add(candidate!.ToUpperInvariant());
                    }
                }
            }

            macs.Sort(StringComparer.Ordinal);
            return macs.Count > 0 ? macs[0] : null;
        });

        Add(components, "mac", mac);
    }

    /// <summary>Adds the volume serial of the drive Windows is installed on.</summary>
    [SupportedOSPlatform("windows")]
    private void AddSystemVolumeSerial(List<string> components)
    {
        string systemDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))
            ?.TrimEnd('\\') ?? "C:";

        string? serial = Query(() =>
        {
            using ManagementObjectSearcher searcher = new(
                $"SELECT VolumeSerialNumber FROM Win32_LogicalDisk WHERE DeviceID = '{systemDrive}'");
            foreach (ManagementBaseObject item in searcher.Get())
            {
                using (item)
                {
                    string? candidate = item["VolumeSerialNumber"]?.ToString();
                    if (IsMeaningful(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        });

        Add(components, "vol", serial);
    }

    private static void AddPortableComponents(List<string> components)
    {
        Add(components, "os", System.Runtime.InteropServices.RuntimeInformation.OSDescription);
        Add(components, "arch", System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString());
        Add(components, "cpu", Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"));
        Add(components, "cn", Environment.GetEnvironmentVariable("COMPUTERNAME"));
    }

    /// <summary>Runs a WMI query, swallowing any provider/permission failure into a null result.</summary>
    private static string? Query(Func<string?> query)
    {
        try
        {
            return query();
        }
        catch (ManagementException)
        {
            return null;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsMeaningful(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !Placeholders.Contains(value.Trim());

    private static void Add(List<string> components, string label, string? value)
    {
        if (IsMeaningful(value))
        {
            components.Add($"{label}:{value!.Trim()}");
        }
    }
}
