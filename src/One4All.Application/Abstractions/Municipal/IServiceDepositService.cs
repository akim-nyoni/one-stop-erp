using One4All.Core.Common;
using One4All.Core.Municipal;

namespace One4All.Application.Abstractions.Municipal;

/// <summary>A request to take a service deposit.</summary>
public sealed class TakeDepositRequest
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The property.</summary>
    public long PropertyId { get; init; }

    /// <summary>The service the deposit secures.</summary>
    public ServiceType ServiceType { get; init; }

    /// <summary>The amount taken.</summary>
    public decimal Amount { get; init; }

    /// <summary>When it was taken.</summary>
    public DateOnly Date { get; init; }

    /// <summary>The bank or cash account the money is banked to.</summary>
    public string BankAccountCode { get; init; } = string.Empty;

    /// <summary>The liability account the deposit is held on.</summary>
    public string LiabilityAccountCode { get; init; } = string.Empty;
}

/// <summary>A deposit that is held.</summary>
/// <param name="Reference">The deposit reference.</param>
/// <param name="Amount">The amount held.</param>
/// <param name="JournalNumber">The journal that booked it.</param>
public sealed record DepositTaken(string Reference, decimal Amount, string JournalNumber);

/// <summary>A held deposit on a property.</summary>
/// <param name="Reference">The deposit reference.</param>
/// <param name="ServiceType">The service it secures.</param>
/// <param name="Amount">The amount held.</param>
/// <param name="TakenOn">When it was taken.</param>
public sealed record HeldDeposit(string Reference, ServiceType ServiceType, decimal Amount, DateOnly TakenOn);

/// <summary>
/// Service deposits (brief §4.9): money taken when a service is connected and held as a liability, then refunded to the
/// consumer or applied to the account's arrears when the service ends. Each posts through the ledger.
/// </summary>
public interface IServiceDepositService
{
    /// <summary>Takes a deposit and books it as a liability (Dr bank / Cr the liability account), in one unit.</summary>
    /// <param name="request">The deposit.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The deposit, or why it was refused.</returns>
    Task<Result<DepositTaken>> TakeAsync(TakeDepositRequest request, CancellationToken ct = default);

    /// <summary>Refunds a held deposit to the consumer (Dr the liability account / Cr bank), in one unit.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="reference">The deposit.</param>
    /// <param name="date">The refund date.</param>
    /// <param name="bankAccountCode">The bank or cash account refunded from.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The journal number, or why it was refused.</returns>
    Task<Result<string>> RefundAsync(long companyId, string reference, DateOnly date, string bankAccountCode, CancellationToken ct = default);

    /// <summary>
    /// Applies a held deposit to the account's balance (Dr the liability account / Cr the receivables control), leaving a
    /// credit the final bill is settled against. One unit.
    /// </summary>
    /// <param name="companyId">The council.</param>
    /// <param name="reference">The deposit.</param>
    /// <param name="date">The date applied.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The receipt document number, or why it was refused.</returns>
    Task<Result<string>> ApplyToArrearsAsync(long companyId, string reference, DateOnly date, CancellationToken ct = default);

    /// <summary>The deposits held on a property.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The held deposits.</returns>
    Task<IReadOnlyList<HeldDeposit>> HeldAsync(long companyId, long propertyId, CancellationToken ct = default);
}
