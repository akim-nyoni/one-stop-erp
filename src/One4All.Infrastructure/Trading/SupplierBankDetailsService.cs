using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>
/// Dual authorisation of supplier bank-detail changes; when the company defines a workflow for them (brief §7), a request goes to its inbox and
/// is approved or rejected there, with the stages and approvers it sets.
/// </summary>
public sealed class SupplierBankDetailsService : ISupplierBankDetailsService
{
    private readonly One4AllDbContext _db;
    private readonly Lazy<IWorkflowService>? _workflow;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="workflow">The approval workflow.</param>
    public SupplierBankDetailsService(One4AllDbContext db, Lazy<IWorkflowService>? workflow = null)
    {
        _db = db;
        _workflow = workflow;
    }

    /// <summary>The workflow reference of a change.</summary>
    /// <param name="changeId">The change.</param>
    /// <returns>The reference.</returns>
    public static string Reference(long changeId) => "SBC-" + changeId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public Task<Result<long>> RequestChangeAsync(long companyId, long supplierId, SupplierBankDetails details, long requestedByUserId, CancellationToken ct = default)
        => _db.AtomicAsync(() => RequestCoreAsync(companyId, supplierId, details, requestedByUserId, ct), ct);

    private async Task<Result<long>> RequestCoreAsync(long companyId, long supplierId, SupplierBankDetails details, long requestedByUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(details);
        bool walletOnly = string.IsNullOrWhiteSpace(details.AccountNumber);
        if (walletOnly && details.MobileMoneyNumber is null)
        {
            return Error.Validation("Ap.BankDetailsIncomplete", "New bank details need an account number (or a mobile-money wallet).");
        }

        Supplier? supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == supplierId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (supplier is null)
        {
            return Error.NotFound("Ap.SupplierNotFound", $"Supplier {supplierId} not found.");
        }

        if (await _db.Set<SupplierBankChange>().AnyAsync(c => c.SupplierId == supplierId && c.Status == BankChangeStatus.Pending, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Ap.BankChangePending", $"Supplier {supplier.Code} already has a bank-detail change awaiting approval.");
        }

        // A wallet-only change keeps the bank account as it is.
        SupplierBankChange change = walletOnly
            ? new(companyId, supplierId, supplier.BankAccountName, supplier.BankAccountNumber, supplier.BankName, requestedByUserId) { MobileMoneyNumber = details.MobileMoneyNumber!.Trim() }
            : new(companyId, supplierId, Clean(details.AccountName), details.AccountNumber!.Trim(), Clean(details.BankName), requestedByUserId) { MobileMoneyNumber = details.MobileMoneyNumber?.Trim() };
        _db.Set<SupplierBankChange>().Add(change);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        // When the company routes bank-detail changes through a workflow, it starts now; the inbox decision applies or rejects the change.
        Result<bool> governed = await Workflow.WorkflowGate.StartIfDefinedAsync(_db, _workflow, companyId, WorkflowDocumentTypes.SupplierBankChange, Reference(change.Id), 0m, requestedByUserId,
            [("Supplier", supplier.Code), ("Category", supplier.Category), ("Kind", walletOnly ? "Wallet" : "Bank account")], ct).ConfigureAwait(false);
        return governed.IsFailure ? governed.Error : change.Id;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PendingBankChange>> PendingAsync(long companyId, CancellationToken ct = default)
    {
        var rows = await (
            from c in _db.Set<SupplierBankChange>().AsNoTracking()
            join s in _db.Suppliers.AsNoTracking() on c.SupplierId equals s.Id
            where c.CompanyId == companyId && c.Status == BankChangeStatus.Pending
            orderby c.Id
            select new { c, s.Code, s.BankAccountName, s.BankAccountNumber, s.BankName })
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. rows.Select(r => new PendingBankChange(r.c.Id, r.c.SupplierId, r.Code,
            new SupplierBankDetails(r.BankAccountName, r.BankAccountNumber, r.BankName),
            new SupplierBankDetails(r.c.AccountName, r.c.AccountNumber, r.c.BankName),
            r.c.RequestedByUserId))];
    }

    /// <inheritdoc />
    public Task<Result> ApproveAsync(long companyId, long changeId, long approverUserId, CancellationToken ct = default)
        => ApproveCoreAsync(companyId, changeId, approverUserId, fromWorkflow: false, ct);

    // From the workflow the inbox decision is the approval; directly, a change the workflow governs is refused.
    internal async Task<Result> ApproveCoreAsync(long companyId, long changeId, long approverUserId, bool fromWorkflow, CancellationToken ct)
    {
        Result<SupplierBankChange> found = await FindPendingAsync(companyId, changeId, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        if (!fromWorkflow && await Workflow.WorkflowGate.InProgressAsync(_db, companyId, WorkflowDocumentTypes.SupplierBankChange, Reference(changeId), ct).ConfigureAwait(false))
        {
            return Result.Failure(Workflow.WorkflowGate.Governed(WorkflowDocumentTypes.SupplierBankChange, Reference(changeId)));
        }

        SupplierBankChange change = found.Value;
        if (!change.Approve(approverUserId))
        {
            return Result.Failure(Error.Rule("Ap.SelfApproval", "A different user must approve a bank-detail change than the one who requested it."));
        }

        Supplier supplier = await _db.Suppliers.FirstAsync(s => s.Id == change.SupplierId, ct).ConfigureAwait(false);
        supplier.ChangeBankDetails(change);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result> RejectAsync(long companyId, long changeId, long userId, string reason, CancellationToken ct = default)
        => RejectCoreAsync(companyId, changeId, userId, reason, fromWorkflow: false, ct);

    internal async Task<Result> RejectCoreAsync(long companyId, long changeId, long userId, string reason, bool fromWorkflow, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("Ap.ReasonRequired", "Rejecting a bank-detail change requires a reason."));
        }

        Result<SupplierBankChange> found = await FindPendingAsync(companyId, changeId, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        if (!fromWorkflow && await Workflow.WorkflowGate.InProgressAsync(_db, companyId, WorkflowDocumentTypes.SupplierBankChange, Reference(changeId), ct).ConfigureAwait(false))
        {
            return Result.Failure(Workflow.WorkflowGate.Governed(WorkflowDocumentTypes.SupplierBankChange, Reference(changeId)));
        }

        found.Value.Reject(userId, reason.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<Result<SupplierBankChange>> FindPendingAsync(long companyId, long changeId, CancellationToken ct)
    {
        SupplierBankChange? change = await _db.Set<SupplierBankChange>().FirstOrDefaultAsync(c => c.Id == changeId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (change is null)
        {
            return Error.NotFound("Ap.BankChangeNotFound", $"Bank-detail change {changeId} not found.");
        }

        if (change.Status != BankChangeStatus.Pending)
        {
            return Error.Rule("Ap.BankChangeNotPending", $"Bank-detail change {changeId} is already {change.Status}.");
        }

        return change;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Applies the end of a bank-detail change's workflow: approved → the supplier's details change; otherwise the change is rejected.</summary>
internal sealed class SupplierBankChangeWorkflowHandler : IWorkflowDocumentHandler
{
    private readonly SupplierBankDetailsService _bank;

    public SupplierBankChangeWorkflowHandler(SupplierBankDetailsService bank) => _bank = bank;

    public string DocumentType => WorkflowDocumentTypes.SupplierBankChange;

    public Task<Result> ConcludeAsync(long companyId, string documentReference, Core.Workflow.WorkflowStatus outcome, long decidedByUserId, string? comment, CancellationToken ct = default)
    {
        if (!documentReference.StartsWith("SBC-", StringComparison.Ordinal)
            || !long.TryParse(documentReference.AsSpan(4), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long changeId))
        {
            return Task.FromResult(Result.Failure(Error.Validation("Ap.BankChangeReference", $"{documentReference} is not a bank-detail change reference.")));
        }

        return outcome == Core.Workflow.WorkflowStatus.Approved
            ? _bank.ApproveCoreAsync(companyId, changeId, decidedByUserId, fromWorkflow: true, ct)
            : _bank.RejectCoreAsync(companyId, changeId, decidedByUserId, string.IsNullOrWhiteSpace(comment) ? $"Rejected in the workflow ({outcome})" : comment, fromWorkflow: true, ct);
    }
}
