using Microsoft.EntityFrameworkCore;
using One4All.Core.Municipal;
using One4All.Core.Receipting;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Reporting;

/// <summary>
/// Billed against collected for the municipal revenue reports (M150, RC9, RC49): posted bills by bill date, ward and service, and committed
/// receipts by date, the payer's ward (their first property) and the service each line pays for.
/// </summary>
internal static class CollectionAnalysis
{
    /// <summary>The ward shown for money from a payer with no property.</summary>
    public const string NoWard = "(no property)";

    /// <summary>One month, ward and service.</summary>
    /// <param name="Month">The month (yyyy-MM).</param>
    /// <param name="Ward">The ward.</param>
    /// <param name="Service">The service.</param>
    /// <param name="Billed">What was billed.</param>
    /// <param name="Collected">What was collected.</param>
    public sealed record Cell(string Month, string Ward, string Service, decimal Billed, decimal Collected)
    {
        /// <summary>Gets collected as a percentage of billed (null when nothing was billed).</summary>
        public decimal? Efficiency => Billed == 0m ? null : decimal.Round(Collected * 100m / Billed, 1, MidpointRounding.AwayFromZero);
    }

    /// <summary>Works the cells out over a period.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="companyId">The company.</param>
    /// <param name="fromDate">The first day.</param>
    /// <param name="toDate">The last day.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cells, by month, ward and service.</returns>
    public static async Task<List<Cell>> ComputeAsync(One4AllDbContext db, long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, string> wardOf = (await db.Properties.AsNoTracking().Where(p => p.CompanyId == companyId).Select(p => new { p.RatepayerCustomerId, p.Ward, p.StandNumber })
                .ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(p => p.RatepayerCustomerId).ToDictionary(g => g.Key, g => g.OrderBy(p => p.StandNumber, StringComparer.Ordinal).First().Ward);

        var billed = await (from l in db.Set<BillingRunLine>().AsNoTracking()
                            join r in db.Set<BillingRun>().AsNoTracking() on l.BillingRunId equals r.Id
                            join p in db.Properties.AsNoTracking() on l.PropertyId equals p.Id
                            where r.CompanyId == companyId && l.IsPosted && r.BillDate >= fromDate && r.BillDate <= toDate
                            select new { r.BillDate, p.Ward, r.ServiceType, l.Charge }).ToListAsync(ct).ConfigureAwait(false);
        var collected = await (from r in db.Receipts.AsNoTracking()
                               from line in r.Lines
                               where r.CompanyId == companyId && r.Status == ReceiptStatus.Committed && r.Date >= fromDate && r.Date <= toDate
                               select new { r.Date, r.PayerAccountId, line.Service, line.Amount }).ToListAsync(ct).ConfigureAwait(false);

        static string Month(DateOnly date) => $"{date.Year:D4}-{date.Month:D2}";
        Dictionary<(string Month, string Ward, string Service), (decimal Billed, decimal Collected)> cells = [];
        void Add((string, string, string) key, decimal billedAmount, decimal collectedAmount)
        {
            (decimal b, decimal c) = cells.GetValueOrDefault(key);
            cells[key] = (b + billedAmount, c + collectedAmount);
        }

        foreach (var bill in billed)
        {
            Add((Month(bill.BillDate), string.IsNullOrWhiteSpace(bill.Ward) ? NoWard : bill.Ward, bill.ServiceType.ToString()), bill.Charge, 0m);
        }

        foreach (var paid in collected)
        {
            string ward = paid.PayerAccountId is long payer && wardOf.TryGetValue(payer, out string? w) && !string.IsNullOrWhiteSpace(w) ? w : NoWard;
            string service = string.IsNullOrWhiteSpace(paid.Service) ? "(unspecified)"
                : Enum.TryParse(paid.Service.Trim(), true, out ServiceType known) ? known.ToString() : paid.Service.Trim();
            Add((Month(paid.Date), ward, service), 0m, paid.Amount);
        }

        return [.. cells.OrderBy(c => c.Key.Month, StringComparer.Ordinal).ThenBy(c => c.Key.Ward, StringComparer.Ordinal).ThenBy(c => c.Key.Service, StringComparer.Ordinal)
            .Select(c => new Cell(c.Key.Month, c.Key.Ward, c.Key.Service, c.Value.Billed, c.Value.Collected))];
    }
}
