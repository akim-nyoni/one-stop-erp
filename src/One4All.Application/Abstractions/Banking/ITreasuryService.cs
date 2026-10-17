using One4All.Core.Common;

namespace One4All.Application.Abstractions.Banking;

/// <summary>A transfer between two of the company's bank accounts.</summary>
public sealed class BankTransferRequest
{
    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The account paid from.</summary>
    public long FromBankAccountId { get; init; }

    /// <summary>The account paid into.</summary>
    public long ToBankAccountId { get; init; }

    /// <summary>The transfer date.</summary>
    public DateOnly Date { get; init; }

    /// <summary>The amount leaving, in the paying account's currency.</summary>
    public decimal AmountSent { get; init; }

    /// <summary>The amount arriving, in the receiving account's currency; needed when the currencies differ.</summary>
    public decimal? AmountReceived { get; init; }

    /// <summary>The transfer reference.</summary>
    public string Reference { get; init; } = string.Empty;

    /// <summary>The clearing account that carries a cross-currency transfer between its two legs.</summary>
    public string? ClearingAccountCode { get; init; }

    /// <summary>Where a gain on a cross-currency transfer goes.</summary>
    public string? FxGainAccountCode { get; init; }

    /// <summary>Where a loss on a cross-currency transfer goes.</summary>
    public string? FxLossAccountCode { get; init; }
}

/// <summary>What a transfer posted.</summary>
/// <param name="JournalNumbers">The journals: one for a same-currency transfer; a leg each, and the exchange difference, across currencies.</param>
/// <param name="BaseSent">The base value that left.</param>
/// <param name="BaseReceived">The base value that arrived.</param>
public sealed record BankTransferResult(IReadOnlyList<string> JournalNumbers, decimal BaseSent, decimal BaseReceived)
{
    /// <summary>The realised exchange gain (positive) or loss (negative).</summary>
    public decimal FxGainLoss => BaseReceived - BaseSent;
}

/// <summary>
/// Treasury transfers between the company's bank accounts (brief §4.7). A same-currency transfer is one journal. A
/// cross-currency transfer posts each leg in its own currency at the day's rate through a clearing account, and the
/// difference between the base value sent and received is a realised exchange gain or loss - all in one unit.
/// </summary>
public interface ITreasuryService
{
    /// <summary>Transfers between two bank accounts.</summary>
    /// <param name="request">The transfer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was posted, or why it was refused.</returns>
    Task<Result<BankTransferResult>> TransferAsync(BankTransferRequest request, CancellationToken ct = default);
}
