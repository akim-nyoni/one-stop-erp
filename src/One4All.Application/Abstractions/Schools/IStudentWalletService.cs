using One4All.Core.Common;

namespace One4All.Application.Abstractions.Schools;

/// <summary>Student spending wallets for the tuck shop (brief §14 J 185): money held for the student until spent.</summary>
public interface IStudentWalletService
{
    /// <summary>Tops up a wallet: Dr the bank / cash account, Cr the wallet liability.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="studentId">The student.</param>
    /// <param name="date">The date.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="bankAccountCode">The account the money came into.</param>
    /// <param name="walletAccountCode">The wallet liability account.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The new balance, or an error.</returns>
    Task<Result<decimal>> TopUpAsync(long companyId, long studentId, DateOnly date, decimal amount, string bankAccountCode, string walletAccountCode, CancellationToken ct = default);

    /// <summary>
    /// Spends from a wallet at the tuck shop: Dr the wallet liability, Cr tuck-shop sales. Refused beyond the balance, beyond the student's
    /// daily limit, or for a student no longer enrolled.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="studentId">The student.</param>
    /// <param name="date">The date.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="description">What was bought.</param>
    /// <param name="walletAccountCode">The wallet liability account.</param>
    /// <param name="salesAccountCode">The tuck-shop sales account.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The new balance, or an error.</returns>
    Task<Result<decimal>> SpendAsync(long companyId, long studentId, DateOnly date, decimal amount, string description, string walletAccountCode, string salesAccountCode, CancellationToken ct = default);
}
