using One4All.Core.Common;

namespace One4All.Core.Vouchers;

/// <summary>What an allowance voucher is for.</summary>
public enum AllowanceKind
{
    /// <summary>Fuel.</summary>
    Fuel = 1,

    /// <summary>Meals.</summary>
    Meal = 2,

    /// <summary>Any other allowance.</summary>
    Other = 3,
}

/// <summary>Where an allowance voucher stands.</summary>
public enum AllowanceVoucherStatus
{
    /// <summary>Issued and not yet redeemed.</summary>
    Issued = 0,

    /// <summary>Redeemed at a supplier and matched to its invoice.</summary>
    Redeemed = 1,

    /// <summary>Cancelled before use.</summary>
    Cancelled = 2,
}

/// <summary>
/// A fuel, meal or other allowance voucher (<c>vou.allowance_voucher</c>, brief §4.8): issued to a member of staff or a
/// contractor under a serial number, redeemed at a supplier, and reconciled against that supplier's invoice so every
/// voucher billed is one that was issued, unexpired and not billed before.
/// </summary>
public sealed class AllowanceVoucher : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private AllowanceVoucher()
    {
        Serial = string.Empty;
        IssuedTo = string.Empty;
    }

    /// <summary>Issues a voucher.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="serial">The serial number.</param>
    /// <param name="kind">What it is for.</param>
    /// <param name="faceValue">Its value.</param>
    /// <param name="issuedTo">Who it was issued to.</param>
    /// <param name="issuedOn">When.</param>
    /// <param name="expiresOn">The last day it can be used.</param>
    public AllowanceVoucher(long companyId, string serial, AllowanceKind kind, decimal faceValue, string issuedTo, DateOnly issuedOn, DateOnly expiresOn)
    {
        CompanyId = companyId;
        Serial = serial;
        Kind = kind;
        FaceValue = faceValue;
        IssuedTo = issuedTo;
        IssuedOn = issuedOn;
        ExpiresOn = expiresOn;
        Status = AllowanceVoucherStatus.Issued;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The serial number.</summary>
    public string Serial { get; private set; }

    /// <summary>What it is for.</summary>
    public AllowanceKind Kind { get; private set; }

    /// <summary>Its value.</summary>
    public decimal FaceValue { get; private set; }

    /// <summary>Who it was issued to.</summary>
    public string IssuedTo { get; private set; }

    /// <summary>When it was issued.</summary>
    public DateOnly IssuedOn { get; private set; }

    /// <summary>The last day it can be used.</summary>
    public DateOnly ExpiresOn { get; private set; }

    /// <summary>Where it stands.</summary>
    public AllowanceVoucherStatus Status { get; private set; }

    /// <summary>The supplier it was redeemed at.</summary>
    public long? RedeemedAtSupplierId { get; private set; }

    /// <summary>The date it was used.</summary>
    public DateOnly? RedeemedOn { get; private set; }

    /// <summary>The supplier invoice it was billed on.</summary>
    public string? SupplierInvoiceNumber { get; private set; }

    /// <summary>Marks the voucher redeemed against a supplier invoice.</summary>
    /// <param name="supplierId">The supplier.</param>
    /// <param name="usedOn">When it was used.</param>
    /// <param name="invoiceNumber">The invoice it was billed on.</param>
    public void Redeem(long supplierId, DateOnly usedOn, string invoiceNumber)
    {
        Status = AllowanceVoucherStatus.Redeemed;
        RedeemedAtSupplierId = supplierId;
        RedeemedOn = usedOn;
        SupplierInvoiceNumber = invoiceNumber;
    }

    /// <summary>Cancels an unused voucher.</summary>
    public void Cancel() => Status = AllowanceVoucherStatus.Cancelled;
}
