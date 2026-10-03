using One4All.Application.Trading;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Trading;

/// <summary>Reconciles suppliers' statements to the purchase ledger (brief §4.6).</summary>
public interface ISupplierStatementService
{
    /// <summary>
    /// Reconciles a supplier's open-item statement to our outstanding AP items for that supplier (in
    /// transaction currency), pairing by document number, then by amount within the date window.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="supplierId">The supplier.</param>
    /// <param name="statement">The supplier's statement lines (invoices positive, credits / payments negative).</param>
    /// <param name="dateToleranceDays">Days either side for amount-only pairing.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reconciliation, or not-found.</returns>
    Task<Result<SupplierReconciliation>> ReconcileAsync(long companyId, long supplierId, IReadOnlyList<SupplierStatementLine> statement, int dateToleranceDays = SupplierStatementReconciler.DefaultDateToleranceDays, CancellationToken ct = default);
}
