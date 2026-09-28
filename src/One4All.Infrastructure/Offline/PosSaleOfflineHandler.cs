using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Offline;
using One4All.Application.Abstractions.Pos;
using One4All.Core.Common;
using One4All.Core.Offline;
using One4All.Core.Pos;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Offline;

/// <summary>
/// Replays a <see cref="PosSaleEnvelope"/> captured offline by posting it through <see cref="IPosService"/>.
/// It is idempotent by the terminal-generated sale number: if a sale with that number is already present it
/// returns success without posting again, so an at-least-once redelivery (e.g. a crash between posting and
/// marking the operation synced) never doubles a journal.
/// </summary>
public sealed class PosSaleOfflineHandler : IOfflineOperationHandler
{
    private readonly One4AllDbContext _db;
    private readonly IPosService _pos;

    /// <summary>Creates the handler.</summary>
    /// <param name="db">The database context (for the idempotency check).</param>
    /// <param name="pos">The POS service that posts the sale.</param>
    public PosSaleOfflineHandler(One4AllDbContext db, IPosService pos)
    {
        _db = db;
        _pos = pos;
    }

    /// <inheritdoc />
    public string Kind => PosSaleEnvelope.Kind;

    /// <inheritdoc />
    public async Task<Result> ReplayAsync(OfflineOperation operation, CancellationToken ct = default)
    {
        PosSaleEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<PosSaleEnvelope>(operation.PayloadJson);
        }
        catch (JsonException ex)
        {
            return Result.Failure(Error.Validation("Offline.PosSale.Payload", $"Malformed PosSale payload: {ex.Message}"));
        }

        if (envelope is null)
        {
            return Result.Failure(Error.Validation("Offline.PosSale.Payload", "The PosSale payload was empty."));
        }

        // Idempotency: if this sale number already posted, treat the replay as a success without re-posting.
        bool alreadyPosted = await _db.Set<Sale>()
            .AnyAsync(s => s.CompanyId == envelope.CompanyId && s.SaleNumber == envelope.SaleNumber, ct)
            .ConfigureAwait(false);
        if (alreadyPosted)
        {
            return Result.Success();
        }

        Result<CompleteSaleResult> result = await _pos.CompleteSaleAsync(envelope.ToRequest(), ct).ConfigureAwait(false);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }
}
