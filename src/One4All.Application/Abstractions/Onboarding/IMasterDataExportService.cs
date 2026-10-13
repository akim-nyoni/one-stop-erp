using One4All.Core.Common;

namespace One4All.Application.Abstractions.Onboarding;

/// <summary>
/// Master-data export (brief §10 "import / export"): the chart of accounts, warehouses, customers, suppliers, items and employees written in the
/// same CSV layout their importers read, so a register can be taken out, corrected in a spreadsheet and loaded into another company.
/// </summary>
public interface IMasterDataExportService
{
    /// <summary>The kinds that can be exported.</summary>
    IReadOnlyList<string> Kinds { get; }

    /// <summary>Exports a register as CSV in its import layout.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="kind">Accounts, Warehouses, Customers, Suppliers, Items or Employees.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The CSV, or why there is none.</returns>
    Task<Result<string>> ExportAsync(long companyId, string kind, CancellationToken ct = default);
}
