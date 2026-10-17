using One4All.Core.Common;
using One4All.Core.Vouchers;

namespace One4All.Application.Abstractions.Vouchers;

/// <summary>A run of vouchers to issue under consecutive serial numbers.</summary>
/// <param name="Prefix">The serial prefix.</param>
/// <param name="FirstNumber">The first serial number.</param>
/// <param name="Count">How many.</param>
/// <param name="Kind">What they are for.</param>
/// <param name="FaceValue">Each voucher's value.</param>
/// <param name="IssuedTo">Who they are issued to.</param>
/// <param name="IssuedOn">When.</param>
/// <param name="ExpiresOn">The last day they can be used.</param>
public sealed record AllowanceIssueRequest(string Prefix, int FirstNumber, int Count, AllowanceKind Kind, decimal FaceValue, string IssuedTo, DateOnly IssuedOn, DateOnly ExpiresOn);

/// <summary>A voucher billed on a supplier invoice.</summary>
/// <param name="Serial">The serial number.</param>
/// <param name="UsedOn">When it was used.</param>
public sealed record BilledVoucher(string Serial, DateOnly UsedOn);

/// <summary>The result of reconciling a supplier invoice against the vouchers it bills.</summary>
/// <param name="InvoiceNumber">The supplier invoice.</param>
/// <param name="VouchersMatched">Vouchers accepted and marked redeemed.</param>
/// <param name="VoucherValue">Their face value.</param>
/// <param name="InvoicedAmount">What the supplier invoiced.</param>
public sealed record VoucherReconciliation(string InvoiceNumber, int VouchersMatched, decimal VoucherValue, decimal InvoicedAmount)
{
    /// <summary>Invoiced less the vouchers' value; nil when the invoice is right.</summary>
    public decimal Difference => InvoicedAmount - VoucherValue;
}

/// <summary>Vouchers issued and not yet billed, by person and kind.</summary>
/// <param name="IssuedTo">Who holds them.</param>
/// <param name="Kind">What they are for.</param>
/// <param name="Count">How many.</param>
/// <param name="Value">Their value.</param>
/// <param name="Expired">How many have expired unused.</param>
public sealed record OutstandingVouchers(string IssuedTo, AllowanceKind Kind, int Count, decimal Value, int Expired);

/// <summary>
/// Fuel, meal and allowance vouchers (brief §4.8): issued in serial-numbered runs, then reconciled against supplier
/// invoices - every voucher billed must have been issued, be unexpired when used and not have been billed before, and the
/// invoice must equal the vouchers' value.
/// </summary>
public interface IAllowanceVoucherService
{
    /// <summary>Issues a run of vouchers.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="request">The run.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The serial numbers issued, or why the run was refused.</returns>
    Task<Result<IReadOnlyList<string>>> IssueAsync(long companyId, AllowanceIssueRequest request, CancellationToken ct = default);

    /// <summary>Reconciles a supplier invoice against the vouchers it bills, all or nothing.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="supplierId">The supplier.</param>
    /// <param name="invoiceNumber">The supplier's invoice number.</param>
    /// <param name="invoicedAmount">What the invoice charges.</param>
    /// <param name="vouchers">The vouchers billed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reconciliation, or the first voucher or amount that does not agree.</returns>
    Task<Result<VoucherReconciliation>> ReconcileInvoiceAsync(long companyId, long supplierId, string invoiceNumber, decimal invoicedAmount, IReadOnlyList<BilledVoucher> vouchers, CancellationToken ct = default);

    /// <summary>The vouchers outstanding at a date, by holder and kind.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The date, for expiry.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The outstanding vouchers.</returns>
    Task<IReadOnlyList<OutstandingVouchers>> OutstandingAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default);
}
