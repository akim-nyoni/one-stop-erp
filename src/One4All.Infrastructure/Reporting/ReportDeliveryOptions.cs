namespace One4All.Infrastructure.Reporting;

/// <summary>Where scheduled reports may be delivered as files (brief §13.4 deliver by email/folder/printer).</summary>
public sealed class ReportDeliveryOptions
{
    /// <summary>The root every delivery folder sits under (a subscription names a folder relative to it, never outside it).</summary>
    public string FolderRoot { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "One4All", "ReportDelivery");

    /// <summary>Resolves a subscription's folder under the root, or null when it would escape it.</summary>
    /// <param name="folder">The relative folder.</param>
    /// <returns>The full path, or null.</returns>
    public string? Resolve(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || Path.IsPathRooted(folder))
        {
            return null;
        }

        string root = Path.GetFullPath(FolderRoot);
        string full = Path.GetFullPath(Path.Combine(root, folder));
        string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}
