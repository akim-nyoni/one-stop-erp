using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Offline;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Pos;
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

/// <summary>Replays a void captured offline: the sale must have synced first (a redelivery after the void is a no-op).</summary>
public sealed class PosVoidOfflineHandler : IOfflineOperationHandler
{
    private readonly One4AllDbContext _db;
    private readonly IPosService _pos;

    /// <summary>Creates the handler.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="pos">The POS service.</param>
    public PosVoidOfflineHandler(One4AllDbContext db, IPosService pos)
    {
        _db = db;
        _pos = pos;
    }

    /// <inheritdoc />
    public string Kind => PosVoidEnvelope.Kind;

    /// <inheritdoc />
    public async Task<Result> ReplayAsync(OfflineOperation operation, CancellationToken ct = default)
    {
        (PosVoidEnvelope? envelope, Result? problem) = OfflineEnvelopes.Read<PosVoidEnvelope>(operation, "PosVoid");
        if (envelope is null)
        {
            return problem!;
        }

        Sale? sale = await _db.Set<Sale>().AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == envelope.CompanyId && s.SaleNumber == envelope.SaleNumber, ct).ConfigureAwait(false);
        if (sale is null)
        {
            return Result.Failure(Error.Conflict("Offline.PosVoid.SalePending", $"Sale {envelope.SaleNumber} has not synced yet, so it cannot be voided."));
        }

        if (sale.Status == SaleStatus.Voided)
        {
            return Result.Success();
        }

        Result<string> voided = await _pos.VoidSaleAsync(envelope.CompanyId, sale.Id, envelope.Date, envelope.Reason, envelope.TaxAccountCode, ct).ConfigureAwait(false);
        return voided.IsSuccess ? Result.Success() : Result.Failure(voided.Error);
    }
}

/// <summary>Replays a full refund captured offline against the sale's lines once the sale has synced; the reason's offline reference makes it idempotent.</summary>
public sealed class PosRefundOfflineHandler : IOfflineOperationHandler
{
    private readonly One4AllDbContext _db;
    private readonly IPosEventService _events;

    /// <summary>Creates the handler.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="events">Refunds.</param>
    public PosRefundOfflineHandler(One4AllDbContext db, IPosEventService events)
    {
        _db = db;
        _events = events;
    }

    /// <inheritdoc />
    public string Kind => PosRefundEnvelope.Kind;

    /// <inheritdoc />
    public async Task<Result> ReplayAsync(OfflineOperation operation, CancellationToken ct = default)
    {
        (PosRefundEnvelope? envelope, Result? problem) = OfflineEnvelopes.Read<PosRefundEnvelope>(operation, "PosRefund");
        if (envelope is null)
        {
            return problem!;
        }

        Sale? sale = await _db.Set<Sale>().AsNoTracking().Include(s => s.Lines).FirstOrDefaultAsync(s => s.CompanyId == envelope.CompanyId && s.SaleNumber == envelope.SaleNumber, ct)
            .ConfigureAwait(false);
        if (sale is null)
        {
            return Result.Failure(Error.Conflict("Offline.PosRefund.SalePending", $"Sale {envelope.SaleNumber} has not synced yet, so it cannot be refunded."));
        }

        if (await _db.PosEvents.AnyAsync(e => e.CompanyId == envelope.CompanyId && e.SaleNumber == envelope.SaleNumber && e.Kind == PosEventKind.Refund && e.Reason == envelope.Reason, ct)
            .ConfigureAwait(false))
        {
            return Result.Success();
        }

        long? session = envelope.TillSessionId;
        if (envelope.TillSessionId == 0 && !string.IsNullOrWhiteSpace(envelope.OfflineSessionKey))
        {
            session = await _db.Set<TillSession>().Where(s => s.CompanyId == envelope.CompanyId && s.OfflineKey == envelope.OfflineSessionKey).Select(s => (long?)s.Id).FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (session is null)
            {
                return Result.Failure(Error.Conflict("Offline.PosRefund.SessionPending", $"The till the refund on {envelope.SaleNumber} was paid from has not synced yet."));
            }
        }

        Result<PosRefundResult> refunded = await _events.RefundAsync(new PosRefundRequest
        {
            CompanyId = envelope.CompanyId, SaleId = sale.Id, TerminalId = envelope.TerminalId, CashierUserId = envelope.CashierUserId, AuthorisedByUserId = envelope.AuthorisedByUserId,
            Date = envelope.Date, RefundAccountCode = envelope.RefundAccountCode, TaxAccountCode = envelope.TaxAccountCode, Reason = envelope.Reason,
            Lines = [.. sale.Lines.Select(l => new PosRefundLine(l.Id, l.Quantity))], TillSessionId = session == 0 ? null : session,
        }, ct).ConfigureAwait(false);
        return refunded.IsSuccess ? Result.Success() : Result.Failure(refunded.Error);
    }
}

/// <summary>Replays a cash-up (or plain close) captured offline once the till has synced; a session already closed is left as it is.</summary>
public sealed class PosCashUpOfflineHandler : IOfflineOperationHandler
{
    private readonly One4AllDbContext _db;
    private readonly IPosService _pos;

    /// <summary>Creates the handler.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="pos">The POS service.</param>
    public PosCashUpOfflineHandler(One4AllDbContext db, IPosService pos)
    {
        _db = db;
        _pos = pos;
    }

    /// <inheritdoc />
    public string Kind => PosCashUpEnvelope.Kind;

    /// <inheritdoc />
    public async Task<Result> ReplayAsync(OfflineOperation operation, CancellationToken ct = default)
    {
        (PosCashUpEnvelope? envelope, Result? problem) = OfflineEnvelopes.Read<PosCashUpEnvelope>(operation, "PosCashUp");
        if (envelope is null)
        {
            return problem!;
        }

        TillSession? session = envelope.TillSessionId != 0
            ? await _db.Set<TillSession>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == envelope.TillSessionId && s.CompanyId == envelope.CompanyId, ct).ConfigureAwait(false)
            : await _db.Set<TillSession>().AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == envelope.CompanyId && s.OfflineKey == envelope.OfflineSessionKey, ct).ConfigureAwait(false);
        if (session is null)
        {
            return Result.Failure(Error.Conflict("Offline.PosCashUp.SessionPending", "The till being closed has not synced yet."));
        }

        if (session.Status == TillSessionStatus.Closed)
        {
            return Result.Success();
        }

        // Its sales go first: a sale still queued would otherwise post into a closed till.
        if (await _db.OfflineOperations.AnyAsync(o => o.CompanyId == envelope.CompanyId && o.Kind == PosSaleEnvelope.Kind && o.Status == OfflineOperationStatus.Pending && o.Id != operation.Id, ct)
            .ConfigureAwait(false))
        {
            return Result.Failure(Error.Conflict("Offline.PosCashUp.SalesPending", "Sales rung up in the till are still syncing; the till closes after them."));
        }

        if (envelope.Counts.Count == 0 || string.IsNullOrWhiteSpace(envelope.CashAccountCode) || string.IsNullOrWhiteSpace(envelope.OverShortAccountCode))
        {
            Result<TillRead> closed = await _pos.CloseTillAsync(session.Id, ct).ConfigureAwait(false);
            return closed.IsSuccess ? Result.Success() : Result.Failure(closed.Error);
        }

        Result declared = await _pos.DeclareCountAsync(session.Id, [.. envelope.Counts.Select(c => new CashCount(c.Currency, c.Amount))], envelope.CountedByUserId, ct).ConfigureAwait(false);
        if (declared.IsFailure && declared.Error.Code != "Pos.CountDeclared")
        {
            return declared;
        }

        Result<CashUpResult> cashedUp = await _pos.CashUpAndCloseAsync(new CashUpRequest
        {
            TillSessionId = session.Id, Date = envelope.Date, CashAccountCode = envelope.CashAccountCode.Trim(), OverShortAccountCode = envelope.OverShortAccountCode.Trim(),
            CountedByUserId = envelope.CountedByUserId,
        }, ct).ConfigureAwait(false);
        return cashedUp.IsSuccess ? Result.Success() : Result.Failure(cashedUp.Error);
    }
}

/// <summary>Reads an operation's payload, naming the kind in any complaint.</summary>
internal static class OfflineEnvelopes
{
    /// <summary>Deserialises the payload.</summary>
    /// <typeparam name="T">The envelope type.</typeparam>
    /// <param name="operation">The operation.</param>
    /// <param name="kind">The kind, for the message.</param>
    /// <returns>The envelope, or the failure to return.</returns>
    public static (T? Envelope, Result? Problem) Read<T>(OfflineOperation operation, string kind)
        where T : class
    {
        try
        {
            T? envelope = JsonSerializer.Deserialize<T>(operation.PayloadJson);
            return envelope is null ? (null, Result.Failure(Error.Validation($"Offline.{kind}.Payload", $"The {kind} payload was empty."))) : (envelope, null);
        }
        catch (JsonException ex)
        {
            return (null, Result.Failure(Error.Validation($"Offline.{kind}.Payload", $"Malformed {kind} payload: {ex.Message}")));
        }
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
