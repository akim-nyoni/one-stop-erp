using One4All.Core.Common;

namespace One4All.Application.Abstractions.MasterData;

/// <summary>What the sample data loader put in.</summary>
/// <param name="CompanyId">The company loaded.</param>
/// <param name="Loaded">One line per kind of record, with the count.</param>
public sealed record SampleDataSummary(long CompanyId, IReadOnlyList<string> Loaded);

/// <summary>
/// Fills a company with realistic sample records across the modules — products with stock, customers with invoices and receipts, suppliers with
/// invoices, employees on the payroll, rated properties with meters and readings, a housing scheme with stands and applicants, a bank account, a
/// cashier station and a few assets — so every screen has something to show while the system is being tried out. Every record is marked by
/// the <c>SMP-</c> prefix on its code; loading twice is refused.
/// </summary>
public interface ISampleDataService
{
    /// <summary>Loads the sample records into a company.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was loaded, or why not (<c>Sample.AlreadyLoaded</c> when the company has sample records already).</returns>
    Task<Result<SampleDataSummary>> LoadAsync(long companyId, CancellationToken ct = default);
}
