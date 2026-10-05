using One4All.Core.Common;

namespace One4All.Application.Abstractions.Banking;

/// <summary>A bank layout to save.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="Code">The layout code.</param>
/// <param name="Name">The bank / format name.</param>
/// <param name="HeaderTemplate">The header line template, if any.</param>
/// <param name="DetailTemplate">The per-payment template.</param>
/// <param name="TrailerTemplate">The trailer line template, if any.</param>
public sealed record BankFileLayoutRequest(long CompanyId, string Code, string Name, string? HeaderTemplate, string DetailTemplate, string? TrailerTemplate);

/// <summary>One EFT file: a bank's payments in one currency.</summary>
/// <param name="Bank">The beneficiaries' bank.</param>
/// <param name="Currency">The currency.</param>
/// <param name="FileName">A file name for it.</param>
/// <param name="Count">The number of payments.</param>
/// <param name="Total">The control total.</param>
/// <param name="Hash">The hash total of the account numbers.</param>
/// <param name="Content">The file text.</param>
public sealed record BankFileOutput(string Bank, string Currency, string FileName, int Count, decimal Total, long Hash, string Content);

/// <summary>
/// Bank payment files (brief §4.15.7): layouts are stored templates (see <c>BankFileFormatter</c>), and a payroll run's net pay is
/// written as one file per beneficiary bank and currency, with control total and hash, honouring split pay.
/// </summary>
public interface IBankFileService
{
    /// <summary>Saves (creates or replaces) a layout; refused when a template uses an unknown field (<c>Bank.LayoutField</c>).</summary>
    /// <param name="request">The layout.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The layout id.</returns>
    Task<Result<long>> SaveLayoutAsync(BankFileLayoutRequest request, CancellationToken ct = default);

    /// <summary>
    /// Writes a payroll run's bank files in a layout: each bank-paid employee's net pay (bank-transfer splits to their own accounts, the
    /// rest to the main account), held salaries left out. Refused when an employee paid by bank has no account
    /// (<c>Bank.MissingAccount</c>).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="runNumber">The run's document or journal number.</param>
    /// <param name="layoutCode">The layout.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The files, one per bank and currency.</returns>
    Task<Result<IReadOnlyList<BankFileOutput>>> PayrollFilesAsync(long companyId, string runNumber, string layoutCode, CancellationToken ct = default);
}
