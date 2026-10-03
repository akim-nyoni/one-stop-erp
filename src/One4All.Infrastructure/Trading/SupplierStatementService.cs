using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Trading;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>Loads a supplier's outstanding AP items and reconciles them to the supplier's statement.</summary>
public sealed class SupplierStatementService : ISupplierStatementService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public SupplierStatementService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<SupplierReconciliation>> ReconcileAsync(long companyId, long supplierId, IReadOnlyList<SupplierStatementLine> statement, int dateToleranceDays = SupplierStatementReconciler.DefaultDateToleranceDays, CancellationToken ct = default)
    {
        bool exists = await _db.Suppliers.AnyAsync(s => s.Id == supplierId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (!exists)
        {
            return Error.NotFound("Ap.SupplierNotFound", $"Supplier {supplierId} not found.");
        }

        // AP credit items (invoices) are what we owe; debit items (payments, debit notes) reduce it.
        var items = await _db.OpenItems
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsPayable && i.TraderId == supplierId && i.OutstandingTransaction != 0m)
            .Select(i => new { i.Id, i.DocumentNumber, i.DocumentDate, i.Sign, i.OutstandingTransaction })
            .ToListAsync(ct).ConfigureAwait(false);

        List<SupplierLedgerItem> ledger = items
            .Select(i => new SupplierLedgerItem(i.Id, i.DocumentNumber, i.DocumentDate, i.Sign == LedgerSign.Credit ? i.OutstandingTransaction : -i.OutstandingTransaction))
            .ToList();

        return SupplierStatementReconciler.Reconcile(statement, ledger, dateToleranceDays);
    }
}
