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

        // A sale rung up in a till opened offline finds its session by the till's offline key (the till syncs first).
        CompleteSaleRequest request = envelope.ToRequest();
        if (envelope.TillSessionId == 0 && !string.IsNullOrWhiteSpace(envelope.OfflineSessionKey))
        {
            long? session = await _db.Set<TillSession>().Where(s => s.CompanyId == envelope.CompanyId && s.OfflineKey == envelope.OfflineSessionKey)
                .Select(s => (long?)s.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (session is null)
            {
                return Result.Failure(Error.Conflict("Offline.PosSale.SessionPending", $"The till sale {envelope.SaleNumber} was rung up in has not synced yet."));
            }

            request = (envelope with { TillSessionId = session.Value }).ToRequest();
        }

        Result<CompleteSaleResult> result = await _pos.CompleteSaleAsync(request, ct).ConfigureAwait(false);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }
}

/// <summary>Replays a till opened offline (idempotent on its offline key).</summary>
public sealed class PosOpenTillOfflineHandler : IOfflineOperationHandler
{
    private readonly IPosService _pos;

    /// <summary>Creates the handler.</summary>
    /// <param name="pos">The POS service.</param>
    public PosOpenTillOfflineHandler(IPosService pos) => _pos = pos;

    /// <inheritdoc />
    public string Kind => PosOpenTillEnvelope.Kind;

    /// <inheritdoc />
    public async Task<Result> ReplayAsync(OfflineOperation operation, CancellationToken ct = default)
    {
        PosOpenTillEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<PosOpenTillEnvelope>(operation.PayloadJson);
        }
        catch (JsonException ex)
        {
            return Result.Failure(Error.Validation("Offline.PosOpenTill.Payload", $"Malformed PosOpenTill payload: {ex.Message}"));
        }

        if (envelope is null)
        {
            return Result.Failure(Error.Validation("Offline.PosOpenTill.Payload", "The PosOpenTill payload was empty."));
        }

        Result<long> opened = await _pos.OpenTillOfflineAsync(envelope.CompanyId, envelope.TerminalId, envelope.CashierUserId, envelope.OpeningFloat, envelope.OpenedUtc, envelope.OfflineKey, ct)
            .ConfigureAwait(false);
        return opened.IsSuccess ? Result.Success() : Result.Failure(opened.Error);
    }
}
