using One4All.Core.Common;
using One4All.Core.Vouchers;

namespace One4All.Application.Abstractions.Vouchers;

/// <summary>A line of a voucher to prepare.</summary>
/// <param name="AccountCode">The account.</param>
/// <param name="Amount">The amount; for a journal voucher, positive for a debit and negative for a credit.</param>
/// <param name="Narrative">The line description.</param>
/// <param name="CostCentre">The cost centre the line posts to.</param>
/// <param name="Department">The department the line posts to.</param>
/// <param name="Project">The project the line posts to.</param>
/// <param name="TaxCode">On a payment, receipt or petty cash voucher, the tax code when the amount includes VAT: the net posts to the account, the VAT to the code's account.</param>
public sealed record VoucherLineRequest(string AccountCode, decimal Amount, string? Narrative = null, string? CostCentre = null, string? Department = null, string? Project = null,
    string? TaxCode = null);

/// <summary>A voucher to prepare.</summary>
/// <param name="Type">The kind.</param>
/// <param name="Date">The voucher date.</param>
/// <param name="Party">Who is paid or pays (or what a journal is about).</param>
/// <param name="CashAccountCode">The bank or cash account, for payment, receipt and petty cash vouchers.</param>
/// <param name="Narrative">The description.</param>
/// <param name="Lines">The lines: what is paid for, received for, or the journal's debits and credits.</param>
/// <param name="PreparedByUserId">Who prepares it.</param>
public sealed record VoucherRequest(AccountingVoucherType Type, DateOnly Date, string Party, string? CashAccountCode, string? Narrative, IReadOnlyList<VoucherLineRequest> Lines, long PreparedByUserId);

/// <summary>A prepared voucher.</summary>
/// <param name="VoucherId">The voucher.</param>
/// <param name="Number">Its number.</param>
/// <param name="Total">Its amount.</param>
public sealed record PreparedVoucher(long VoucherId, string Number, decimal Total);

/// <summary>
/// Payment, receipt, journal and petty cash vouchers (brief §4.8.3): numbered per type, prepared by one user, approved
/// and posted by another, cancelled while still drafts, and printed with signature blocks.
/// </summary>
public interface IAccountingVoucherService
{
    /// <summary>Prepares a voucher, numbering it from its type's sequence (PV, RV, JV, PCV).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="request">The voucher.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The prepared voucher, or why it was refused.</returns>
    Task<Result<PreparedVoucher>> PrepareAsync(long companyId, VoucherRequest request, CancellationToken ct = default);

    /// <summary>Approves and posts a voucher; never by the preparer.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="voucherId">The voucher.</param>
    /// <param name="approverUserId">The approver.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The journal number, or why it was refused.</returns>
    Task<Result<string>> ApproveAsync(long companyId, long voucherId, long approverUserId, CancellationToken ct = default);

    /// <summary>
    /// Checks a draft voucher - the "checked by" signature between preparer and approver. The checker is neither the preparer nor (later)
    /// the approver.
    /// </summary>
    Task<Result> CheckAsync(long companyId, long voucherId, long checkerUserId, CancellationToken ct = default);

    /// <summary>Cancels a draft voucher; its number stays used.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="voucherId">The voucher.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> CancelAsync(long companyId, long voucherId, CancellationToken ct = default);

    /// <summary>Prints the voucher as text.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="voucherId">The voucher.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The printed voucher, or why there is none.</returns>
    Task<Result<string>> RenderAsync(long companyId, long voucherId, CancellationToken ct = default);
}
