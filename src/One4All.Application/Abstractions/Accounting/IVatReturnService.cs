using One4All.Core.Common;

namespace One4All.Application.Abstractions.Accounting;

/// <summary>A request to settle (post) a VAT return for a period.</summary>
public sealed class SettleVatRequest
{
    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The start of the return period.</summary>
    public DateOnly FromDate { get; init; }

    /// <summary>The end of the return period (the settlement posting date).</summary>
    public DateOnly ToDate { get; init; }

    /// <summary>The VAT payable/refundable control account the net is posted to (owed to the tax authority).</summary>
    public string VatPayableAccountCode { get; init; } = string.Empty;

    /// <summary>The settlement document number.</summary>
    public string DocumentNumber { get; init; } = string.Empty;
}

/// <summary>The result of settling a VAT return.</summary>
/// <param name="OutputTax">Output tax for the period (cleared from the VAT-output control accounts).</param>
/// <param name="InputTax">Input tax for the period (cleared from the VAT-input control accounts).</param>
/// <param name="NetPayable">Output − input: positive is payable to the authority, negative is refundable.</param>
/// <param name="JournalNumber">The settlement journal number, or null when there was nothing to settle.</param>
public readonly record struct VatSettlementResult(decimal OutputTax, decimal InputTax, decimal NetPayable, string? JournalNumber);

/// <summary>
/// Settles a VAT return for a period (brief §3.6): clears the accumulated balances on the VAT-output and
/// VAT-input control accounts and posts the net to a VAT payable/refundable account owed to the tax
/// authority, so the control accounts start the next period clean. Because the settlement posts back into
/// those control accounts within the period, re-running for a settled period nets to zero (nothing to post).
/// </summary>
public interface IVatReturnService
{
    /// <summary>Settles the VAT return for the period, posting the clearing journal.</summary>
    /// <param name="request">The settlement request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The settlement result, or an error.</returns>
    Task<Result<VatSettlementResult>> SettleAsync(SettleVatRequest request, CancellationToken ct = default);
}
