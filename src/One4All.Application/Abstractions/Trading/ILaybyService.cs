using One4All.Core.Common;

namespace One4All.Application.Abstractions.Trading;

/// <summary>Goods to set aside on a lay-bye.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="UnitPrice">The unit price agreed.</param>
public sealed record LaybyLineRequest(long ItemId, decimal Quantity, decimal UnitPrice);

/// <summary>A new lay-bye.</summary>
public sealed class LaybyRequest
{
    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The customer.</summary>
    public long CustomerId { get; init; }

    /// <summary>The plan number.</summary>
    public string PlanNumber { get; init; } = string.Empty;

    /// <summary>Where the goods are set aside.</summary>
    public long WarehouseId { get; init; }

    /// <summary>When it is opened.</summary>
    public DateOnly OpenedOn { get; init; }

    /// <summary>The date by which it must be paid.</summary>
    public DateOnly FinalDate { get; init; }

    /// <summary>The deposit paid now.</summary>
    public decimal Deposit { get; init; }

    /// <summary>The smallest deposit accepted, as a percentage of the price.</summary>
    public decimal MinimumDepositPercent { get; init; }

    /// <summary>The share of what is paid kept if the plan is cancelled or defaults.</summary>
    public decimal ForfeitPercent { get; init; }

    /// <summary>The bank or till account receiving the deposit.</summary>
    public string BankAccountCode { get; init; } = string.Empty;

    /// <summary>The liability holding lay-bye payments.</summary>
    public string DepositAccountCode { get; init; } = string.Empty;

    /// <summary>The revenue account credited when the plan completes.</summary>
    public string RevenueAccountCode { get; init; } = string.Empty;

    /// <summary>The income account credited with forfeitures.</summary>
    public string ForfeitureAccountCode { get; init; } = string.Empty;

    /// <summary>The goods.</summary>
    public IReadOnlyList<LaybyLineRequest> Lines { get; init; } = [];

    /// <summary>Gets the tax code the prices include; output tax is accounted for when the sale completes and the goods leave (null: untaxed).</summary>
    public string? TaxCode { get; init; }
}

/// <summary>The outcome of a lay-bye payment.</summary>
/// <param name="Paid">Paid to date.</param>
/// <param name="Balance">Still to pay.</param>
/// <param name="Completed">Whether the payment completed the plan, making the sale and releasing the goods.</param>
public sealed record LaybyPayment(decimal Paid, decimal Balance, bool Completed);

/// <summary>The outcome of cancelling a lay-bye.</summary>
/// <param name="PlanNumber">The plan.</param>
/// <param name="Paid">What had been paid.</param>
/// <param name="Forfeited">What was kept.</param>
/// <param name="Refunded">What was refunded.</param>
public sealed record LaybyCancellation(string PlanNumber, decimal Paid, decimal Forfeited, decimal Refunded);

/// <summary>
/// Lay-bye plans (brief §4.5): goods set aside against a deposit and instalments, held out of available stock, sold only
/// when paid in full; cancellation or default forfeits the agreed share of what was paid and refunds the rest.
/// </summary>
public interface ILaybyService
{
    /// <summary>Opens a plan and takes the deposit (Dr bank, Cr lay-bye deposits).</summary>
    /// <param name="request">The plan.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The plan id, or why it was refused.</returns>
    Task<Result<long>> OpenAsync(LaybyRequest request, CancellationToken ct = default);

    /// <summary>
    /// Takes an instalment. The payment that clears the balance completes the plan in the same unit: the deposits become
    /// revenue and the goods are issued from stock.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="planId">The plan.</param>
    /// <param name="date">The payment date.</param>
    /// <param name="amount">The amount; no more than the balance.</param>
    /// <param name="bankAccountCode">The bank or till account.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Where the plan stands, or why it was refused.</returns>
    Task<Result<LaybyPayment>> PayAsync(long companyId, long planId, DateOnly date, decimal amount, string bankAccountCode, CancellationToken ct = default);

    /// <summary>Cancels an open plan: the forfeiture goes to income and the rest is refunded.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="planId">The plan.</param>
    /// <param name="date">The cancellation date.</param>
    /// <param name="refundAccountCode">The account the refund is paid from or owed through.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The forfeiture and refund, or why it was refused.</returns>
    Task<Result<LaybyCancellation>> CancelAsync(long companyId, long planId, DateOnly date, string refundAccountCode, CancellationToken ct = default);

    /// <summary>Cancels every open plan whose final date has passed unpaid, applying its forfeiture.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The date.</param>
    /// <param name="refundAccountCode">The account refunds are owed through.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The plans cancelled, or why the run failed (and changed nothing).</returns>
    Task<Result<IReadOnlyList<LaybyCancellation>>> DefaultOverdueAsync(long companyId, DateOnly asAtDate, string refundAccountCode, CancellationToken ct = default);
}
