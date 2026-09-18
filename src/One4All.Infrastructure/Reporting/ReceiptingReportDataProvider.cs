using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Receipting;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Reporting;

/// <summary>
/// Supplies live rows for the Cashiering &amp; Receipting (<c>RC</c>) reports. A representative set is backed
/// by real queries today (receipt day book, suspense register, deposits held, agent reconciliation,
/// stationery control, import batches); the remaining RC commands return no rows so their layout still
/// renders until a dedicated query is wired.
/// </summary>
public sealed class ReceiptingReportDataProvider : IReportDataProvider
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context.</param>
    public ReceiptingReportDataProvider(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public bool CanHandle(string command) => command.StartsWith("RC", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        return command.ToUpperInvariant() switch
        {
            "RC4" => await ReceiptDayBookAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC29" => await SuspenseRegisterAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC31" => await ImportBatchesAsync(companyId, ct).ConfigureAwait(false),
            "RC40" => await DepositsHeldAsync(companyId, ct).ConfigureAwait(false),
            "RC45" => await AgentReconciliationAsync(companyId, ct).ConfigureAwait(false),
            "RC20" => await StationeryControlAsync(companyId, ct).ConfigureAwait(false),
            _ => [],
        };
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReceiptDayBookAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly from = ReportParameters.TryDate(parameters, "FromDate") ?? DateOnly.MinValue;
        DateOnly to = ReportParameters.TryDate(parameters, "ToDate") ?? DateOnly.MaxValue;
        List<Receipt> receipts = await _db.Set<Receipt>().Include(r => r.Lines).Include(r => r.Tenders)
            .Where(r => r.CompanyId == companyId && r.Date >= from && r.Date <= to)
            .OrderBy(r => r.ReceiptNumber)
            .ToListAsync(ct).ConfigureAwait(false);

        return receipts.Select(r => Row(new()
        {
            ["ReceiptNumber"] = r.ReceiptNumber,
            ["Payer"] = r.PayerKind == PayerKind.Anonymous ? "(sundry)" : $"{r.PayerKind} #{r.PayerAccountId}",
            ["Tender"] = r.Tenders.FirstOrDefault()?.Type.ToString() ?? string.Empty,
            ["Amount"] = r.Lines.Sum(l => l.Amount),
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SuspenseRegisterAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly asAt = ReportParameters.Date(parameters, "AsAtDate");
        var lines = await _db.Set<ImportBatch>()
            .Where(b => b.CompanyId == companyId)
            .SelectMany(b => b.Lines)
            .Where(l => l.Status == ImportLineStatus.Unmatched)
            .Select(l => new { l.PayerReference, l.PayerName, l.Amount, l.ValueDate })
            .ToListAsync(ct).ConfigureAwait(false);

        return lines.Select(l => Row(new()
        {
            ["Reference"] = l.PayerReference,
            ["PayerName"] = l.PayerName,
            ["Days"] = Math.Max(0, asAt.DayNumber - l.ValueDate.DayNumber),
            ["Amount"] = l.Amount,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ImportBatchesAsync(long companyId, CancellationToken ct)
    {
        List<ImportBatch> batches = await _db.Set<ImportBatch>().Include(b => b.Lines)
            .Where(b => b.CompanyId == companyId)
            .OrderByDescending(b => b.ImportDate)
            .ToListAsync(ct).ConfigureAwait(false);

        return batches.Select(b => Row(new()
        {
            ["FileReference"] = b.FileReference,
            ["Matched"] = b.MatchedCount,
            ["Unmatched"] = b.UnmatchedCount,
            ["TotalAmount"] = b.TotalAmount,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DepositsHeldAsync(long companyId, CancellationToken ct)
    {
        // Deposits held = committed receipt lines of type Deposit (the deposit liability was raised on them).
        var deposits = await _db.Set<Receipt>()
            .Where(r => r.CompanyId == companyId && r.Status == ReceiptStatus.Committed)
            .SelectMany(r => r.Lines.Where(l => l.Type == ReceiptLineType.Deposit).Select(l => new { r.ReceiptNumber, r.PayerKind, r.PayerAccountId, r.Date, l.Amount, l.Narrative }))
            .ToListAsync(ct).ConfigureAwait(false);

        return deposits.Select(d => Row(new()
        {
            ["Payer"] = d.PayerKind == PayerKind.Anonymous ? d.ReceiptNumber : $"{d.PayerKind} #{d.PayerAccountId}",
            ["Type"] = d.Narrative ?? "Deposit",
            ["ReceivedOn"] = d.Date.ToDateTime(TimeOnly.MinValue),
            ["Amount"] = d.Amount,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AgentReconciliationAsync(long companyId, CancellationToken ct)
    {
        List<Agent> agents = await _db.Set<Agent>()
            .Where(a => a.CompanyId == companyId)
            .OrderBy(a => a.Code)
            .ToListAsync(ct).ConfigureAwait(false);

        return agents.Select(a => Row(new()
        {
            ["Agent"] = $"{a.Code} — {a.Name}",
            ["Collected"] = a.CollectedToDate,
            ["Remitted"] = a.RemittedToDate,
            ["Outstanding"] = a.Outstanding,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> StationeryControlAsync(long companyId, CancellationToken ct)
    {
        List<StationeryBook> books = await _db.Set<StationeryBook>().Include(b => b.Captures)
            .Where(b => b.CompanyId == companyId)
            .OrderBy(b => b.BookNumber)
            .ToListAsync(ct).ConfigureAwait(false);

        return books.Select(b => Row(new()
        {
            ["BookNumber"] = b.BookNumber,
            ["Range"] = $"{b.FromSerial}-{b.ToSerial}",
            ["Used"] = b.Captures.Count,
            ["Unaccounted"] = b.UnusedSerials().Count,
        })).ToList();
    }

    private static IReadOnlyDictionary<string, object?> Row(Dictionary<string, object?> values) => values;
}
