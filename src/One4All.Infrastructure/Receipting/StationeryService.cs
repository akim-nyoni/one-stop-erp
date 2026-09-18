using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Receipting;
using One4All.Core.Common;
using One4All.Core.Receipting;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Receipting;

/// <summary>
/// Pre-numbered manual stationery control (brief §RCP-11). Registers receipt books by serial range and
/// captures manual receipts by their original serial, so gaps and unreturned books are provable.
/// </summary>
public sealed class StationeryService : IStationeryService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public StationeryService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<long>> IssueBookAsync(long companyId, string bookNumber, long fromSerial, long toSerial, long issuedToUserId, DateOnly date, CancellationToken ct = default)
    {
        if (toSerial < fromSerial)
        {
            return Error.Validation("Rcp.InvalidSerialRange", "The last serial cannot precede the first.");
        }

        StationeryBook book = new(companyId, bookNumber, fromSerial, toSerial, issuedToUserId, date);
        _db.Set<StationeryBook>().Add(book);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return book.Id;
    }

    /// <inheritdoc />
    public async Task<Result<ManualCaptureResult>> CaptureManualReceiptAsync(long companyId, long bookId, long serial, decimal amount, DateOnly receiptDate, CancellationToken ct = default)
    {
        if (amount <= 0m)
        {
            return Error.Rule("Rcp.NonPositiveAmount", "The manual receipt amount must be positive.");
        }

        StationeryBook? book = await _db.Set<StationeryBook>().Include(b => b.Captures)
            .FirstOrDefaultAsync(b => b.Id == bookId && b.CompanyId == companyId, ct).ConfigureAwait(false);
        if (book is null)
        {
            return Error.NotFound("Rcp.BookNotFound", $"Stationery book {bookId} not found.");
        }

        try
        {
            book.Capture(serial, amount, receiptDate);
        }
        catch (InvalidOperationException ex)
        {
            return Error.Rule("Rcp.SerialRejected", ex.Message);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new ManualCaptureResult(book.Id, serial, book.UnusedSerials().Count);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<long>>> GetUnusedSerialsAsync(long companyId, long bookId, CancellationToken ct = default)
    {
        StationeryBook? book = await _db.Set<StationeryBook>().Include(b => b.Captures)
            .FirstOrDefaultAsync(b => b.Id == bookId && b.CompanyId == companyId, ct).ConfigureAwait(false);
        return book is null
            ? Error.NotFound("Rcp.BookNotFound", $"Stationery book {bookId} not found.")
            : Result<IReadOnlyList<long>>.Success(book.UnusedSerials());
    }
}
