using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.FixedAssets;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Reporting;

/// <summary>Serves the FA- and TX-numbered reports (brief §14 M, N): every data command whose name starts with <c>Fatx</c>.</summary>
public sealed class AssetTaxReportDataProvider : IReportDataProvider
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context.</param>
    public AssetTaxReportDataProvider(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public bool CanHandle(string command) => command.StartsWith("Fatx", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        DateOnly toDate = DateParameter(parameters, "ToDate") ?? today;
        DateOnly fromDate = DateParameter(parameters, "FromDate") ?? new DateOnly(toDate.Year, 1, 1);
        return command.ToUpperInvariant() switch
        {
            "FATXDEPRECIATION" => await DepreciationAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "FATXADDITIONS" => await AdditionsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "FATXMOVEMENT" => await MovementAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "FATXFULLYDEPRECIATED" => await FullyDepreciatedAsync(companyId, ct).ConfigureAwait(false),
            "FATXVATDETAIL" => await VatDetailAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "FATXVATRECON" => await VatReconAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "FATXNONSTANDARD" => await NonStandardAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "FATXWHTRETURN" => await WhtReturnAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            _ => [],
        };
    }

    private IQueryable<PostedLine> Posted(long companyId, DateOnly fromDate, DateOnly toDate)
        => from l in _db.Set<JournalLine>().AsNoTracking()
           join j in _db.Journals.AsNoTracking() on l.JournalEntryId equals j.Id
           where j.CompanyId == companyId && (j.Status == JournalStatus.Posted || j.Status == JournalStatus.Reversed) && j.PostingDate >= fromDate && j.PostingDate <= toDate
           select new PostedLine
           {
               Date = j.PostingDate, Journal = j.JournalNumber, DocumentType = j.DocumentType, DocumentNumber = j.DocumentNumber, AccountCode = l.AccountCode, TaxCode = l.TaxCode,
               Narrative = l.Narrative, Debit = l.BaseDebit, Credit = l.BaseCredit,
           };

    // A class with init properties (not a positional record) so EF can translate filters on the projection.
    private sealed class PostedLine
    {
        public DateOnly Date { get; init; }

        public string Journal { get; init; } = string.Empty;

        public string? DocumentType { get; init; }

        public string? DocumentNumber { get; init; }

        public string AccountCode { get; init; } = string.Empty;

        public string? TaxCode { get; init; }

        public string? Narrative { get; init; }

        public decimal Debit { get; init; }

        public decimal Credit { get; init; }
    }

    // The depreciation run's expense lines carry the asset number as their narrative.
    private async Task<List<(DateOnly Date, string Asset, decimal Charge)>> ChargesAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        HashSet<string> expense = [.. await _db.FixedAssets.AsNoTracking().IgnoreQueryFilters().Where(a => a.CompanyId == companyId).Select(a => a.DepreciationExpenseAccountCode).Distinct().ToListAsync(ct).ConfigureAwait(false)];
        return [.. (await Posted(companyId, fromDate, toDate).Where(l => l.DocumentType == "FA-DEPR").ToListAsync(ct).ConfigureAwait(false))
            .Where(l => expense.Contains(l.AccountCode) && l.Narrative != null).Select(l => (l.Date, l.Narrative!, l.Debit - l.Credit))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DepreciationAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<string, FixedAsset> assets = await AssetsAsync(companyId, ct).ConfigureAwait(false);
        return [.. (await ChargesAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false)).GroupBy(c => (Month: c.Date.ToString("yyyy-MM", CultureInfo.InvariantCulture), c.Asset))
            .OrderBy(g => g.Key.Month, StringComparer.Ordinal).ThenBy(g => g.Key.Asset, StringComparer.Ordinal)
            .Select(g => Row(("Month", g.Key.Month), ("Asset", g.Key.Asset), ("Description", assets.GetValueOrDefault(g.Key.Asset)?.Description ?? "?"), ("Class", assets.GetValueOrDefault(g.Key.Asset)?.AssetClass ?? "?"),
                ("Charge", g.Sum(c => c.Charge))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AdditionsAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<string, FixedAsset> assets = await AssetsAsync(companyId, ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [.. assets.Values.Where(a => a.AcquisitionDate >= fromDate && a.AcquisitionDate <= toDate).OrderBy(a => a.AcquisitionDate).ThenBy(a => a.AssetNumber)
            .Select(a => Row(("Kind", "Addition"), ("Asset", a.AssetNumber), ("Description", a.Description), ("Class", a.AssetClass), ("Date", Date(a.AcquisitionDate)),
                ("Detail", $"{a.Method}, {a.UsefulLifeMonths} months"), ("Cost", a.Cost)))];
        DateTimeOffset start = new(fromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), end = new(toDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        Dictionary<string, FixedAsset> byId = assets.Values.ToDictionary(a => a.Id.ToString(CultureInfo.InvariantCulture));
        var changes = await _db.ChangeLog.AsNoTracking().Where(c => c.CompanyId == companyId && c.EntityType == "FixedAsset" && c.Action == Core.Audit.AuditAction.Update
            && c.OccurredOnUtc >= start && c.OccurredOnUtc <= end).OrderBy(c => c.Id).ToListAsync(ct).ConfigureAwait(false);
        rows.AddRange(changes.Where(c => c.ChangedFields.Contains("Location") || c.ChangedFields.Contains("Custodian")).Select(c =>
        {
            FixedAsset? asset = c.EntityId is string id ? byId.GetValueOrDefault(id) : null;
            return Row(("Kind", "Transfer"), ("Asset", asset?.AssetNumber ?? c.EntityId ?? "?"), ("Description", asset?.Description ?? "?"), ("Class", asset?.AssetClass ?? "?"),
                ("Date", c.OccurredOnUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), ("Detail", $"{c.UserName}: {string.Join(", ", c.ChangedFields)} → {c.NewValues}"), ("Cost", 0m));
        }));
        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> MovementAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<string, FixedAsset> assets = await AssetsAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, DateOnly> disposed = await _db.Set<AssetDisposal>().AsNoTracking().Where(d => d.CompanyId == companyId).ToDictionaryAsync(d => d.AssetId, d => d.Date, ct).ConfigureAwait(false);
        List<(DateOnly Date, string Asset, decimal Charge)> charges = await ChargesAsync(companyId, DateOnly.MinValue, toDate, ct).ConfigureAwait(false);
        decimal DepTo(string asset, DateOnly date) => charges.Where(c => c.Asset == asset && c.Date <= date).Sum(c => c.Charge);
        bool HeldOn(FixedAsset a, DateOnly date) => a.AcquisitionDate <= date && !(disposed.TryGetValue(a.Id, out DateOnly d) && d <= date);
        DateOnly before = fromDate.AddDays(-1);
        return [.. assets.Values.GroupBy(a => a.AssetClass).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            List<FixedAsset> opening = [.. g.Where(a => HeldOn(a, before))], closing = [.. g.Where(a => HeldOn(a, toDate))];
            List<FixedAsset> added = [.. g.Where(a => a.AcquisitionDate >= fromDate && a.AcquisitionDate <= toDate)];
            List<FixedAsset> gone = [.. g.Where(a => disposed.TryGetValue(a.Id, out DateOnly d) && d >= fromDate && d <= toDate)];
            decimal depOpening = opening.Sum(a => DepTo(a.AssetNumber, before)), depClosing = closing.Sum(a => DepTo(a.AssetNumber, toDate));
            decimal charge = charges.Where(c => c.Date >= fromDate && g.Any(a => a.AssetNumber == c.Asset)).Sum(c => c.Charge);
            decimal onDisposals = gone.Sum(a => DepTo(a.AssetNumber, disposed[a.Id]));
            decimal costClosing = closing.Sum(a => a.Cost);
            return Row(("Class", g.Key), ("CostOpening", Money(opening.Sum(a => a.Cost))), ("Additions", Money(added.Sum(a => a.Cost))), ("Disposals", Money(gone.Sum(a => a.Cost))),
                ("CostClosing", Money(costClosing)), ("DepOpening", Money(depOpening)), ("Charge", Money(charge)), ("DepDisposals", Money(onDisposals)), ("DepClosing", Money(depClosing)),
                ("Nbv", costClosing - depClosing));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> FullyDepreciatedAsync(long companyId, CancellationToken ct)
        => [.. (await _db.FixedAssets.AsNoTracking().Where(a => a.CompanyId == companyId && a.Status == FixedAssetStatus.FullyDepreciated).OrderBy(a => a.AssetNumber).ToListAsync(ct).ConfigureAwait(false))
            .Select(a => Row(("Asset", a.AssetNumber), ("Description", a.Description), ("Class", a.AssetClass), ("Acquired", Date(a.AcquisitionDate)), ("Location", a.Location ?? "—"),
                ("Custodian", a.Custodian ?? "—"), ("Cost", a.Cost)))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> VatDetailAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
        => [.. (await Posted(companyId, fromDate, toDate).Where(l => l.TaxCode != null).ToListAsync(ct).ConfigureAwait(false))
            .OrderBy(l => l.TaxCode, StringComparer.Ordinal).ThenBy(l => l.Date).ThenBy(l => l.Journal, StringComparer.Ordinal)
            .Select(l => Row(("Date", Date(l.Date)), ("Journal", l.Journal), ("Document", $"{l.DocumentType} {l.DocumentNumber}".Trim()), ("TaxCode", l.TaxCode), ("Account", l.AccountCode),
                ("Narrative", l.Narrative ?? string.Empty), ("Amount", l.Credit - l.Debit)))];

    // Output tax is credit-natural, input tax debit-natural; coded lines on those accounts should account for all of their movement.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> VatReconAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<TaxCode> codes = await _db.TaxCodes.AsNoTracking().Where(t => t.CompanyId == companyId && (t.OutputAccountCode != null || t.InputAccountCode != null)).OrderBy(t => t.Code).ToListAsync(ct).ConfigureAwait(false);
        List<string> accounts = [.. codes.SelectMany(c => new[] { c.OutputAccountCode, c.InputAccountCode }).Where(a => a != null).Select(a => a!).Distinct()];
        List<PostedLine> lines = await Posted(companyId, fromDate, toDate).Where(l => accounts.Contains(l.AccountCode)).ToListAsync(ct).ConfigureAwait(false);
        return [.. codes.Select(c =>
        {
            decimal output = c.OutputAccountCode is null ? 0m : lines.Where(l => l.AccountCode == c.OutputAccountCode).Sum(l => l.Credit - l.Debit);
            decimal input = c.InputAccountCode is null ? 0m : lines.Where(l => l.AccountCode == c.InputAccountCode).Sum(l => l.Debit - l.Credit);
            decimal coded = lines.Where(l => l.TaxCode == c.Code && (l.AccountCode == c.OutputAccountCode || l.AccountCode == c.InputAccountCode))
                .Sum(l => l.AccountCode == c.OutputAccountCode ? l.Credit - l.Debit : -(l.Debit - l.Credit));
            return Row(("TaxCode", c.Code), ("Name", c.Name), ("Output", Money(output)), ("Input", Money(input)), ("Coded", Money(coded)), ("Difference", output - input - coded));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> NonStandardAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<string, TaxCode> codes = await _db.TaxCodes.AsNoTracking().Where(t => t.CompanyId == companyId && (t.Type == TaxType.Zero || t.Type == TaxType.Exempt || t.Type == TaxType.OutOfScope))
            .ToDictionaryAsync(t => t.Code, ct).ConfigureAwait(false);
        List<string> wanted = [.. codes.Keys];
        return [.. (await Posted(companyId, fromDate, toDate).Where(l => l.TaxCode != null && wanted.Contains(l.TaxCode)).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(l => l.TaxCode!).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => Row(("TaxCode", g.Key), ("Name", codes[g.Key].Name), ("Type", codes[g.Key].Type.ToString()), ("Lines", g.Count()), ("Amount", g.Sum(l => l.Credit - l.Debit))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> WhtReturnAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
        => [.. (await _db.Set<WithholdingCertificate>().AsNoTracking().Where(c => c.CompanyId == companyId && c.PaymentDate >= fromDate && c.PaymentDate <= toDate).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(c => (Month: c.PaymentDate.ToString("yyyy-MM", CultureInfo.InvariantCulture), c.TaxCode)).OrderBy(g => g.Key.Month, StringComparer.Ordinal).ThenBy(g => g.Key.TaxCode, StringComparer.Ordinal)
            .Select(g => Row(("Month", g.Key.Month), ("TaxCode", g.Key.TaxCode), ("Certificates", g.Count()), ("Gross", Money(g.Sum(c => c.GrossAmount))),
                ("Remitted", Money(g.Where(c => c.RemittedOn != null).Sum(c => c.WithheldAmount))), ("Withheld", g.Sum(c => c.WithheldAmount))))];

    private Task<Dictionary<string, FixedAsset>> AssetsAsync(long companyId, CancellationToken ct)
        => _db.FixedAssets.AsNoTracking().IgnoreQueryFilters().Where(a => a.CompanyId == companyId).ToDictionaryAsync(a => a.AssetNumber, ct);

    private static DateOnly? DateParameter(IReadOnlyDictionary<string, object?> parameters, string name) => parameters.TryGetValue(name, out object? value) && value is DateOnly date ? date : null;

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Money(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] cells)
    {
        Dictionary<string, object?> row = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, object? value) in cells)
        {
            row[key] = value;
        }

        return row;
    }
}
