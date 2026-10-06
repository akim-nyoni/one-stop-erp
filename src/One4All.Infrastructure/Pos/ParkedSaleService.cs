using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Pos;
using One4All.Core.Common;
using One4All.Core.Pos;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Pos;

/// <summary>Parked sales: hold, list, resume at any till, discard (brief §4.1).</summary>
public sealed class ParkedSaleService : IParkedSaleService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public ParkedSaleService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<long>> ParkAsync(long companyId, long terminalId, string tag, long userId, IReadOnlyList<PosSaleLineRequest> lines, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tag) || lines.Count == 0)
        {
            return Error.Validation("Pos.ParkInvalid", "A parked sale needs a name, table or customer tag and at least one line.");
        }

        if (!await _db.PosTerminals.AnyAsync(t => t.Id == terminalId && t.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Pos.TerminalNotFound", $"Till {terminalId} not found.");
        }

        ParkedSale parked = new(companyId, terminalId, tag.Trim(), JsonSerializer.Serialize(lines), lines.Sum(l => l.LineNet + l.TaxAmount), userId, DateTimeOffset.UtcNow);
        _db.ParkedSales.Add(parked);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return parked.Id;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ParkedSaleSummary>> ParkedAsync(long companyId, CancellationToken ct = default)
        => [.. (await _db.ParkedSales.AsNoTracking().Where(p => p.CompanyId == companyId && p.Status == ParkedSaleStatus.Parked).OrderBy(p => p.Id).ToListAsync(ct).ConfigureAwait(false))
            .Select(p => new ParkedSaleSummary(p.Id, p.Tag, p.TerminalId, Lines(p).Count, p.Total, p.ParkedAt))];

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<PosSaleLineRequest>>> ResumeAsync(long companyId, long parkedSaleId, long terminalId, long userId, CancellationToken ct = default)
    {
        Result<ParkedSale> parked = await OpenAsync(companyId, parkedSaleId, ct).ConfigureAwait(false);
        if (parked.IsFailure)
        {
            return parked.Error;
        }

        if (!await _db.PosTerminals.AnyAsync(t => t.Id == terminalId && t.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Pos.TerminalNotFound", $"Till {terminalId} not found.");
        }

        parked.Value.Resume(terminalId, userId);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result<IReadOnlyList<PosSaleLineRequest>>.Success(Lines(parked.Value));
    }

    /// <inheritdoc />
    public async Task<Result> DiscardAsync(long companyId, long parkedSaleId, long userId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("Pos.ReasonRequired", "Discarding a parked sale needs a reason."));
        }

        Result<ParkedSale> parked = await OpenAsync(companyId, parkedSaleId, ct).ConfigureAwait(false);
        if (parked.IsFailure)
        {
            return Result.Failure(parked.Error);
        }

        parked.Value.Discard(userId, reason.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private static List<PosSaleLineRequest> Lines(ParkedSale parked) => JsonSerializer.Deserialize<List<PosSaleLineRequest>>(parked.LinesJson) ?? [];

    private async Task<Result<ParkedSale>> OpenAsync(long companyId, long parkedSaleId, CancellationToken ct)
    {
        ParkedSale? parked = await _db.ParkedSales.FirstOrDefaultAsync(p => p.Id == parkedSaleId && p.CompanyId == companyId, ct).ConfigureAwait(false);
        if (parked is null)
        {
            return Error.NotFound("Pos.ParkedNotFound", $"Parked sale {parkedSaleId} not found.");
        }

        return parked.Status == ParkedSaleStatus.Parked ? parked : Error.Rule("Pos.ParkedClosed", $"Parked sale '{parked.Tag}' was already {parked.Status.ToString().ToLowerInvariant()}.");
    }
}
