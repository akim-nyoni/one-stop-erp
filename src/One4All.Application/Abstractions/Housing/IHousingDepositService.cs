using One4All.Core.Common;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A purchase deposit received against an accepted offer.</summary>
public sealed record StandDepositReceipt
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The allocation certificate of the accepted offer.</summary>
    public string CertificateNumber { get; init; } = string.Empty;

    /// <summary>The payer's customer account.</summary>
    public long CustomerId { get; init; }

    /// <summary>The amount.</summary>
    public decimal Amount { get; init; }

    /// <summary>When.</summary>
    public DateOnly Date { get; init; }

    /// <summary>The receipt.</summary>
    public string ReceiptReference { get; init; } = string.Empty;

    /// <summary>The bank account.</summary>
    public string BankAccountCode { get; init; } = string.Empty;

    /// <summary>The deposits-held liability account.</summary>
    public string LiabilityAccountCode { get; init; } = string.Empty;
}

/// <summary>An instalment agreement cancelled at the buyer's request.</summary>
public sealed record AgreementCancellationRequest
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The agreement.</summary>
    public string AgreementNumber { get; init; } = string.Empty;

    /// <summary>When.</summary>
    public DateOnly Date { get; init; }

    /// <summary>Why.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>Occupation rental per month occupied.</summary>
    public decimal MonthlyOccupationRental { get; init; }

    /// <summary>The admin charge, in percent of the cash price.</summary>
    public decimal AdminChargePercent { get; init; }

    /// <summary>The stand's net realisable value on its return to stock.</summary>
    public decimal NetRealisableValue { get; init; }

    /// <summary>Who asked for it.</summary>
    public long RequestedByUserId { get; init; }

    /// <summary>Who approved it.</summary>
    public long ApprovedByUserId { get; init; }

    /// <summary>The stands-available account.</summary>
    public string StandsAvailableAccountCode { get; init; } = string.Empty;

    /// <summary>The gain / loss account that takes the forfeit and bears the refund.</summary>
    public string GainLossAccountCode { get; init; } = string.Empty;

    /// <summary>The bank account the refund is paid from.</summary>
    public string BankAccountCode { get; init; } = string.Empty;
}

/// <summary>A cancellation's outcome.</summary>
/// <param name="Paid">What the buyer paid.</param>
/// <param name="MonthsOccupied">Months occupied.</param>
/// <param name="OccupationRental">Rental for those months.</param>
/// <param name="AdminCharge">The admin charge.</param>
/// <param name="Arrears">Instalments billed and unpaid.</param>
/// <param name="Refund">The refund paid.</param>
/// <param name="RepossessionReference">The repossession that returned the stand.</param>
public sealed record AgreementCancellationResult(decimal Paid, int MonthsOccupied, decimal OccupationRental, decimal AdminCharge, decimal Arrears, decimal Refund, string RepossessionReference);

/// <summary>
/// Purchase deposits and agreement cancellations (brief §14 H51, H53): a deposit against an accepted offer is held as a
/// liability, then applied to the buyer's account (a receipt funded from the liability) or refunded; an instalment
/// agreement cancelled at the buyer's request returns the stand through repossession and refunds what was paid less
/// occupation rental, the admin charge and arrears.
/// </summary>
public interface IHousingDepositService
{
    /// <summary>Receives a purchase deposit.</summary>
    /// <param name="receipt">The deposit.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The journal number.</returns>
    Task<Result<string>> TakeDepositAsync(StandDepositReceipt receipt, CancellationToken ct = default);

    /// <summary>Applies a held deposit to the payer's account.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="certificateNumber">The allocation certificate.</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The journal number.</returns>
    Task<Result<string>> ApplyDepositAsync(long companyId, string certificateNumber, DateOnly date, CancellationToken ct = default);

    /// <summary>Refunds a held deposit.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="certificateNumber">The allocation certificate.</param>
    /// <param name="date">When.</param>
    /// <param name="bankAccountCode">The bank account paid from.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The journal number.</returns>
    Task<Result<string>> RefundDepositAsync(long companyId, string certificateNumber, DateOnly date, string bankAccountCode, CancellationToken ct = default);

    /// <summary>Cancels an instalment agreement and refunds the buyer.</summary>
    /// <param name="request">The cancellation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The outcome.</returns>
    Task<Result<AgreementCancellationResult>> CancelAgreementAsync(AgreementCancellationRequest request, CancellationToken ct = default);
}
