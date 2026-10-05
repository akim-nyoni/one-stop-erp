using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A waiting-list fee received.</summary>
public sealed record ApplicantFeeReceipt
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The applicant's waiting-list number.</summary>
    public string WaitingListNumber { get; init; } = string.Empty;

    /// <summary>What it is for.</summary>
    public ApplicantFeeType FeeType { get; init; }

    /// <summary>The amount.</summary>
    public decimal Amount { get; init; }

    /// <summary>When.</summary>
    public DateOnly Date { get; init; }

    /// <summary>The receipt.</summary>
    public string ReceiptReference { get; init; } = string.Empty;

    /// <summary>The bank or cash account.</summary>
    public string BankAccountCode { get; init; } = string.Empty;

    /// <summary>The fee income account.</summary>
    public string IncomeAccountCode { get; init; } = string.Empty;
}

/// <summary>Transfer duty received from the parties to a transfer.</summary>
public sealed record TransferDutyReceipt
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The transfer application.</summary>
    public string ApplicationNumber { get; init; } = string.Empty;

    /// <summary>The duty rate, in percent of the consideration.</summary>
    public decimal RatePercent { get; init; }

    /// <summary>When it was received.</summary>
    public DateOnly Date { get; init; }

    /// <summary>The receipt.</summary>
    public string ReceiptReference { get; init; } = string.Empty;

    /// <summary>The bank account.</summary>
    public string BankAccountCode { get; init; } = string.Empty;

    /// <summary>The duty payable (liability) account.</summary>
    public string LiabilityAccountCode { get; init; } = string.Empty;
}

/// <summary>
/// Housing fees and duties (brief §14 H27, H63): waiting-list registration and renewal fees taken to income; transfer
/// duty collected as agent on a transfer (once per transfer, on its consideration), held as a liability and remitted.
/// </summary>
public interface IHousingFeeService
{
    /// <summary>Records and posts a waiting-list fee.</summary>
    /// <param name="receipt">The fee.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The journal number.</returns>
    Task<Result<string>> RecordApplicantFeeAsync(ApplicantFeeReceipt receipt, CancellationToken ct = default);

    /// <summary>Records and posts transfer duty received.</summary>
    /// <param name="receipt">The duty.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The duty assessed.</returns>
    Task<Result<decimal>> WithholdTransferDutyAsync(TransferDutyReceipt receipt, CancellationToken ct = default);

    /// <summary>Remits held transfer duty to the revenue authority.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="applicationNumber">The transfer.</param>
    /// <param name="date">When.</param>
    /// <param name="reference">The authority's acknowledgement.</param>
    /// <param name="bankAccountCode">The bank account paid from.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The journal number.</returns>
    Task<Result<string>> RemitTransferDutyAsync(long companyId, string applicationNumber, DateOnly date, string reference, string bankAccountCode, CancellationToken ct = default);
}
