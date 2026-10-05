using One4All.Core.Common;

namespace One4All.Application.Abstractions.Payroll;

/// <summary>An AP invoice raised for a payroll third party.</summary>
/// <param name="Payee">The scheme code or garnishee order reference.</param>
/// <param name="DocumentNumber">The AP invoice number.</param>
/// <param name="AccountCode">The payroll liability cleared into AP.</param>
/// <param name="Amount">The amount.</param>
public readonly record struct RemittanceRaised(string Payee, string DocumentNumber, string AccountCode, decimal Amount);

/// <summary>
/// Third-party payments (brief §4.15.5): after a run, raises the AP invoices for the tax authority, pension, medical and other schemes
/// and garnishee creditors that have an AP supplier — Dr the payroll liability, Cr AP — so the money is paid through the payables
/// ledger and reconciles back to the deductions. Companies that set <c>RaisePayrollRemittances</c> get this on every committed run.
/// </summary>
public interface IPayrollRemittanceService
{
    /// <summary>
    /// Raises a run's third-party invoices (by its document or journal number): one per scheme and liability account, one per garnishee
    /// order. Refused for a reversal or reversed run, a run raised before (<c>Payroll.RemittancesRaised</c>), or nothing to remit
    /// (<c>Payroll.NothingToRemit</c>).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="runNumber">The run.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The invoices raised.</returns>
    Task<Result<IReadOnlyList<RemittanceRaised>>> RaiseRemittancesAsync(long companyId, string runNumber, CancellationToken ct = default);
}
