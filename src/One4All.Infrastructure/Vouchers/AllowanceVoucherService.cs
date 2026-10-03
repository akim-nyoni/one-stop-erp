using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Vouchers;
using One4All.Core.Common;
using One4All.Core.Vouchers;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Vouchers;

/// <summary>Fuel, meal and allowance vouchers.</summary>
public sealed class AllowanceVoucherService : IAllowanceVoucherService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public AllowanceVoucherService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<string>>> IssueAsync(long companyId, AllowanceIssueRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Count is < 1 or > 10_000 || request.FirstNumber < 0 || request.FaceValue <= 0m || string.IsNullOrWhiteSpace(request.IssuedTo)
            || request.ExpiresOn < request.IssuedOn)
        {
            return Error.Validation("Vou.IssueInvalid", "A voucher run needs 1 to 10,000 vouchers with a value, a holder and an expiry after issue.");
        }

        string prefix = request.Prefix.Trim().ToUpperInvariant();
        List<string> serials = [.. Enumerable.Range(request.FirstNumber, request.Count).Select(n => prefix + n.ToString("D6", CultureInfo.InvariantCulture))];
        if (await _db.Set<AllowanceVoucher>().AnyAsync(v => v.CompanyId == companyId && serials.Contains(v.Serial), ct).ConfigureAwait(false))
        {
            return Error.Conflict("Vou.SerialTaken", "Some of those serial numbers have already been issued.");
        }

        foreach (string serial in serials)
        {
            _db.Set<AllowanceVoucher>().Add(new AllowanceVoucher(companyId, serial, request.Kind, request.FaceValue, request.IssuedTo.Trim(), request.IssuedOn, request.ExpiresOn));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result<IReadOnlyList<string>>.Success(serials);
    }

    /// <inheritdoc />
    public async Task<Result<VoucherReconciliation>> ReconcileInvoiceAsync(long companyId, long supplierId, string invoiceNumber, decimal invoicedAmount, IReadOnlyList<BilledVoucher> vouchers, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(vouchers);
        if (vouchers.Count == 0 || string.IsNullOrWhiteSpace(invoiceNumber))
        {
            return Error.Validation("Vou.ReconcileInvalid", "A reconciliation needs the invoice number and the vouchers it bills.");
        }

        List<string> serials = [.. vouchers.Select(v => v.Serial.Trim().ToUpperInvariant())];
        if (serials.Distinct(StringComparer.Ordinal).Count() != serials.Count)
        {
            return Error.Rule("Vou.BilledTwice", "The invoice bills the same voucher more than once.");
        }

        Dictionary<string, AllowanceVoucher> issued = await _db.Set<AllowanceVoucher>()
            .Where(v => v.CompanyId == companyId && serials.Contains(v.Serial))
            .ToDictionaryAsync(v => v.Serial, ct).ConfigureAwait(false);
        foreach (BilledVoucher billed in vouchers)
        {
            string serial = billed.Serial.Trim().ToUpperInvariant();
            if (!issued.TryGetValue(serial, out AllowanceVoucher? voucher))
            {
                return Error.Rule("Vou.NotIssued", $"Voucher {serial} was never issued.");
            }

            if (voucher.Status == AllowanceVoucherStatus.Redeemed)
            {
                return Error.Rule("Vou.AlreadyBilled", $"Voucher {serial} was already billed on {voucher.SupplierInvoiceNumber}.");
            }

            if (voucher.Status == AllowanceVoucherStatus.Cancelled)
            {
                return Error.Rule("Vou.Cancelled", $"Voucher {serial} was cancelled.");
            }

            if (billed.UsedOn > voucher.ExpiresOn || billed.UsedOn < voucher.IssuedOn)
            {
                return Error.Rule("Vou.Expired", $"Voucher {serial} was not valid on {billed.UsedOn:yyyy-MM-dd}.");
            }
        }

        decimal value = issued.Values.Sum(v => v.FaceValue);
        if (invoicedAmount != value)
        {
            return Error.Rule("Vou.InvoiceMismatch", $"Invoice {invoiceNumber} charges {invoicedAmount:0.00} for vouchers worth {value:0.00}.");
        }

        foreach (BilledVoucher billed in vouchers)
        {
            issued[billed.Serial.Trim().ToUpperInvariant()].Redeem(supplierId, billed.UsedOn, invoiceNumber.Trim());
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new VoucherReconciliation(invoiceNumber.Trim(), vouchers.Count, value, invoicedAmount);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutstandingVouchers>> OutstandingAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default)
    {
        var open = await _db.Set<AllowanceVoucher>().AsNoTracking()
            .Where(v => v.CompanyId == companyId && v.Status == AllowanceVoucherStatus.Issued && v.IssuedOn <= asAtDate)
            .Select(v => new { v.IssuedTo, v.Kind, v.FaceValue, v.ExpiresOn })
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. open.GroupBy(v => (v.IssuedTo, v.Kind))
            .OrderBy(g => g.Key.IssuedTo, StringComparer.Ordinal).ThenBy(g => g.Key.Kind)
            .Select(g => new OutstandingVouchers(g.Key.IssuedTo, g.Key.Kind, g.Count(), g.Sum(v => v.FaceValue), g.Count(v => v.ExpiresOn < asAtDate)))];
    }
}
