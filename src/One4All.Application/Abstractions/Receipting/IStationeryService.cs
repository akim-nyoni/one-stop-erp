using One4All.Core.Common;

namespace One4All.Application.Abstractions.Receipting;

/// <summary>The result of capturing a manual receipt.</summary>
/// <param name="BookId">The book.</param>
/// <param name="Serial">The serial captured.</param>
/// <param name="RemainingUnused">The count of serials still unaccounted for in the book.</param>
public readonly record struct ManualCaptureResult(long BookId, long Serial, int RemainingUnused);

/// <summary>
/// Pre-numbered manual stationery control (brief §RCP-11). Registers receipt books by serial range,
/// issues them to a named cashier, and captures every manual receipt with its original serial so gaps and
/// unreturned books are provable.
/// </summary>
public interface IStationeryService
{
    /// <summary>Issues a pre-numbered receipt book to a cashier.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="bookNumber">The book number.</param>
    /// <param name="fromSerial">The first serial (inclusive).</param>
    /// <param name="toSerial">The last serial (inclusive).</param>
    /// <param name="issuedToUserId">The cashier the book is issued to.</param>
    /// <param name="date">The issue date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created book id.</returns>
    Task<Result<long>> IssueBookAsync(long companyId, string bookNumber, long fromSerial, long toSerial, long issuedToUserId, DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// Captures a manual (hand-written) receipt into the system with its original serial and date. Rejects a
    /// serial outside the book's range or one already captured.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="bookId">The book.</param>
    /// <param name="serial">The original serial from the paper receipt.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="receiptDate">The original receipt date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The capture result.</returns>
    Task<Result<ManualCaptureResult>> CaptureManualReceiptAsync(long companyId, long bookId, long serial, decimal amount, DateOnly receiptDate, CancellationToken ct = default);

    /// <summary>Returns the serials of a book not yet captured (the gap/unaccounted report).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="bookId">The book.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The unused serials.</returns>
    Task<Result<IReadOnlyList<long>>> GetUnusedSerialsAsync(long companyId, long bookId, CancellationToken ct = default);
}
