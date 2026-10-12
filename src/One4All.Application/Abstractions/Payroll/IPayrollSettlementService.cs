using One4All.Core.Common;
using One4All.Core.Payroll;

namespace One4All.Application.Abstractions.Payroll;

/// <summary>A payroll run's payment: what was paid by each method from which account.</summary>
/// <param name="DocumentNumber">The run.</param>
/// <param name="JournalNumber">The payment journal.</param>
/// <param name="ByMethod">The net pay by method, and the account it was paid from.</param>
public sealed record PayrollPayment(string DocumentNumber, string JournalNumber, IReadOnlyList<(PayMethod Method, string FundingAccountCode, decimal Amount)> ByMethod)
{
    /// <summary>Gets the net pay paid in other currencies: from which account, the amount in that currency and its base value.</summary>
    public IReadOnlyList<(Core.Accounting.CurrencyCode Currency, string FundingAccountCode, decimal Amount, decimal BaseAmount)> ByCurrency { get; init; } = [];
}

/// <summary>Where a run stands after commit.</summary>
/// <param name="DocumentNumber">The run.</param>
/// <param name="Net">The net pay.</param>
/// <param name="PaidOn">When it was paid, if it was.</param>
/// <param name="ClosedOn">When it was closed, if it was.</param>
/// <param name="NetPayOutstanding">What is left on the net pay control account for the run.</param>
public sealed record PayrollRunStage(string DocumentNumber, decimal Net, DateOnly? PaidOn, DateOnly? ClosedOn, decimal NetPayOutstanding);

/// <summary>
/// The payroll run's last two stages (brief §4.15.6 "Commit → Post to GL → Pay → Close", §4.15.8 "net pay control … reconcile to zero after the payment run"):
/// Pay clears the net pay control account by paying each employee's net from the account for their pay method; Close confirms the control is clear for
/// the run and closes it.
/// </summary>
public interface IPayrollSettlementService
{
    /// <summary>Pays a committed run: one journal debiting the net pay control and crediting each pay method's account with what its employees are paid.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="documentNumber">The run's document number.</param>
    /// <param name="paidOn">The payment date.</param>
    /// <param name="fundingAccounts">The account each pay method is paid from (bank transfers from the bank, cash from the cash account, wallets from the mobile-money float).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The payment, or why it was refused.</returns>
    /// <remarks>
    /// Earnings paid in another currency (an element's paid-in currency) are paid from that currency's account in the currency, converted at the
    /// pay date's daily rate; the rest of each employee's net pay goes by their pay method in the base currency.
    /// </remarks>
    Task<Result<PayrollPayment>> PayAsync(long companyId, string documentNumber, DateOnly paidOn, IReadOnlyDictionary<PayMethod, string> fundingAccounts,
        IReadOnlyDictionary<Core.Accounting.CurrencyCode, string>? currencyAccounts = null, CancellationToken ct = default);

    /// <summary>Closes a paid run once its net pay control has cleared.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="documentNumber">The run.</param>
    /// <param name="closedOn">The date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The run's stage, or why it cannot close.</returns>
    Task<Result<PayrollRunStage>> CloseAsync(long companyId, string documentNumber, DateOnly closedOn, CancellationToken ct = default);

    /// <summary>Where a run stands.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="documentNumber">The run.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The stage, or why there is none.</returns>
    Task<Result<PayrollRunStage>> StageAsync(long companyId, string documentNumber, CancellationToken ct = default);
}
