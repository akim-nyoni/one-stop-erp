using One4All.Application.Payroll;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Payroll;

/// <summary>A termination to work out or carry out.</summary>
public sealed record TerminationRequest(long CompanyId, string EmployeeNumber, TerminationType Type, DateOnly LastDay, string Reason)
{
    /// <summary>The company's terminal policy (notice, working days, gratuity and severance weeks, terminal tax rate).</summary>
    public TerminalPolicy Policy { get; init; } = new(1m, 22m, 2m, 4m, 0m);

    /// <summary>The tax table ordinary final pay is taxed on.</summary>
    public string TaxTableCode { get; init; } = "PAYE";

    /// <summary>The tax table gratuity and severance are taxed on, above the policy's tax-free amount (blank: the policy's flat rate).</summary>
    public string? TerminalTaxTableCode { get; init; }

    /// <summary>When the final pay is paid (defaults to the last day).</summary>
    public DateOnly? PayDate { get; init; }

    /// <summary>Salary, notice and leave pay expense.</summary>
    public string SalaryExpenseAccount { get; init; } = string.Empty;

    /// <summary>Gratuity and severance expense.</summary>
    public string TerminalBenefitsAccount { get; init; } = string.Empty;

    /// <summary>PAYE liability.</summary>
    public string PayeLiabilityAccount { get; init; } = string.Empty;

    /// <summary>Net pay owed (paid by the bank later).</summary>
    public string NetPayAccount { get; init; } = string.Empty;

    /// <summary>Gets the year's bonus, paid for the part of the year served (nil: none).</summary>
    public decimal AnnualBonus { get; init; }

    /// <summary>Gets salary overpaid to the leaver, recovered from the final pay (company property not returned is added from the asset register).</summary>
    public decimal Overpayment { get; init; }

    /// <summary>Gets the account credited with property and overpayments recovered (needed when there are any).</summary>
    public string? RecoveriesAccount { get; init; }

    /// <summary>Who authorised it.</summary>
    public long ApprovedByUserId { get; init; }
}

/// <summary>A clearance item of a leaver.</summary>
public sealed record ClearanceItem(long Id, string Description, bool IsCleared, long? ClearedByUserId, DateOnly? ClearedOn);

/// <summary>A carried-out termination.</summary>
public sealed record TerminationResult(long TerminationId, string? JournalNumber, FinalPayStatement Statement, IReadOnlyList<ClearanceItem> Clearance);

/// <summary>
/// Termination and final pay (brief §4.15.8): the final pay worked out with its working, posted in one journal (loans recovered against
/// their accounts), the employee terminated so later runs leave them out, a clearance checklist, and a certificate of service.
/// </summary>
public interface ITerminationService
{
    /// <summary>Works the final pay out without changing anything.</summary>
    Task<Result<FinalPayStatement>> PreviewAsync(TerminationRequest request, CancellationToken ct = default);

    /// <summary>Terminates the employee and posts the final pay.</summary>
    Task<Result<TerminationResult>> TerminateAsync(TerminationRequest request, CancellationToken ct = default);

    /// <summary>Ticks a clearance item off.</summary>
    Task<Result> ClearItemAsync(long companyId, long itemId, long clearedByUserId, DateOnly date, CancellationToken ct = default);

    /// <summary>The leaver's clearance checklist.</summary>
    Task<Result<IReadOnlyList<ClearanceItem>>> ClearanceAsync(long companyId, string employeeNumber, CancellationToken ct = default);

    /// <summary>
    /// Releases the net final pay to the leaver (Dr the net pay account it was accrued to, Cr the bank) - only once every clearance item is cleared (brief §4.15.8).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeNumber">The leaver.</param>
    /// <param name="date">The payment date.</param>
    /// <param name="bankAccountCode">The bank account paying.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The payment journal (null when nothing was due), or why it is held.</returns>
    Task<Result<string?>> ReleaseFinalPayAsync(long companyId, string employeeNumber, DateOnly date, string bankAccountCode, CancellationToken ct = default);

    /// <summary>The certificate of service (dates, position and reason - not pay).</summary>
    Task<Result<string>> CertificateOfServiceAsync(long companyId, string employeeNumber, CancellationToken ct = default);
}
