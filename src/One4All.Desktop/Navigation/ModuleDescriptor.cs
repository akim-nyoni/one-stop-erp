using One4All.Core.Licensing;

namespace One4All.Desktop.Navigation;

/// <summary>
/// Describes a top-level module for the shell's System Tree and icon grid. A module is shown only when
/// its <see cref="LicenceCode"/> is licensed and the signed-in user has at least one permission in its
/// <see cref="PermissionModule"/> group (brief §18 item 5). The standard Sage-style child groups
/// (Maintenance / Transactions / Enquiries / Reports) hang off each module.
/// </summary>
/// <param name="PermissionModule">The permission-module prefix (e.g. <c>GeneralLedger</c>).</param>
/// <param name="Title">The display title (e.g. "General Ledger").</param>
/// <param name="LicenceCode">The licensing module code that gates it (see <see cref="LicenceModules"/>).</param>
/// <param name="Glyph">A short emoji/character used as the tile icon.</param>
/// <param name="Accent">A hex accent colour for the tile.</param>
public sealed record ModuleDescriptor(
    string PermissionModule,
    string Title,
    string LicenceCode,
    string Glyph,
    string Accent)
{
    /// <summary>The standard Sage-style child groups shown under a module in the tree.</summary>
    public static readonly string[] ChildGroups = ["Maintenance", "Transactions", "Enquiries", "Reports"];
}
