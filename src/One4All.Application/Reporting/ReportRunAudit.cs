using System.Globalization;
using System.Security.Cryptography;
using One4All.Core.Reporting;
using One4All.Core.Security;

namespace One4All.Application.Reporting;

/// <summary>
/// Report run auditing (brief §13.3 security): parameters written in one canonical form so the same run is recognised
/// again, the permission each purpose needs (view, print, export, reprint), the document fingerprint, and the copy number
/// of a print — a print of something already printed is a reprint. Pure and unit-tested.
/// </summary>
public static class ReportRunAudit
{
    /// <summary>Parameters as canonical text: sorted by name, dates as ISO, numbers invariant, empty values left out.</summary>
    /// <param name="parameters">The parameters.</param>
    /// <returns>The text, e.g. <c>AsAtDate=2026-05-31; Branch=HQ</c>.</returns>
    public static string Canonical(IReadOnlyDictionary<string, object?> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return string.Join("; ", parameters
            .Where(p => p.Value is not null && !(p.Value is string s && s.Length == 0))
            .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => $"{p.Key}={Text(p.Value!)}"));
    }

    /// <summary>The permission flag a purpose needs on the report permission.</summary>
    /// <param name="purpose">What the report is run for.</param>
    /// <returns>The flag.</returns>
    public static OperationFlags RequiredFlag(ReportRunPurpose purpose) => purpose switch
    {
        ReportRunPurpose.Print => OperationFlags.Print,
        ReportRunPurpose.Export => OperationFlags.Export,
        ReportRunPurpose.Reprint => OperationFlags.Reprint,
        _ => OperationFlags.View,
    };

    /// <summary>What a print request really is, given how many times the same run was printed before.</summary>
    /// <param name="requested">The purpose asked for.</param>
    /// <param name="earlierPrints">Earlier prints of the same report with the same parameters.</param>
    /// <returns>The purpose to record and, for prints, the copy number.</returns>
    public static (ReportRunPurpose Purpose, int? Copy) Classify(ReportRunPurpose requested, int earlierPrints)
        => requested is ReportRunPurpose.Print or ReportRunPurpose.Reprint
            ? (earlierPrints == 0 ? ReportRunPurpose.Print : ReportRunPurpose.Reprint, earlierPrints + 1)
            : (requested, null);

    /// <summary>A document's SHA-256 fingerprint.</summary>
    /// <param name="content">The document.</param>
    /// <returns>The lowercase hex hash.</returns>
    public static string Fingerprint(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    private static string Text(object value) => value switch
    {
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
