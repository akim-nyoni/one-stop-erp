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
            "RC3" => await DailyCollectionSummaryAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC4" => await ReceiptDayBookAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC5" => await CollectionByVoteAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC7" => await CollectionByTenderAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC8" => await CollectionByCurrencyAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC11" => await CashierProductivityAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC12" => await GlReconciliationAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC14" => await TillVarianceAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC15" => await CancelledReceiptsAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC17" => await GapAnalysisAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC20" => await StationeryControlAsync(companyId, ct).ConfigureAwait(false),
            "RC25" => await DailyBankingSummaryAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC26" => await CashInTransitAgeingAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC28" => await CashMovementsAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC29" => await SuspenseRegisterAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC31" => await ImportBatchesAsync(companyId, ct).ConfigureAwait(false),
            "RC32" => await ImportExceptionsAsync(companyId, ct).ConfigureAwait(false),
            "RC38" => await PaymentHistoryAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC39" => await CreditBalancesAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC40" => await DepositsHeldAsync(companyId, ct).ConfigureAwait(false),
            "RC41" => await RefundsRegisterAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC43" => await TopPayersAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC45" => await AgentReconciliationAsync(companyId, ct).ConfigureAwait(false),
            "RC46" => await AgentAgeingAsync(companyId, ct).ConfigureAwait(false),
            "RC51" => await CollectionTrendAsync(companyId, parameters, ct).ConfigureAwait(false),
            _ => [],
        };
    }

    private async Task<List<Receipt>> CommittedReceiptsAsync(long companyId, DateOnly from, DateOnly to, CancellationToken ct) =>
        await _db.Set<Receipt>().Include(r => r.Lines).Include(r => r.Tenders)
            .Where(r => r.CompanyId == companyId && r.Status == ReceiptStatus.Committed && r.Date >= from && r.Date <= to)
            .ToListAsync(ct).ConfigureAwait(false);

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DailyCollectionSummaryAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly date = ReportParameters.Date(parameters, "Date");
        List<Receipt> receipts = await CommittedReceiptsAsync(companyId, date, date, ct).ConfigureAwait(false);
        return receipts.SelectMany(r => r.Tenders)
            .GroupBy(t => t.Type)
            .OrderBy(g => g.Key.ToString())
            .Select(g => Row(new() { ["Category"] = g.Key.ToString(), ["Count"] = g.Count(), ["Amount"] = g.Sum(t => t.Amount) }))
            .ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CollectionByVoteAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        (DateOnly from, DateOnly to) = Range(parameters);
        List<Receipt> receipts = await CommittedReceiptsAsync(companyId, from, to, ct).ConfigureAwait(false);
        Dictionary<string, string> names = await AccountNamesAsync(companyId, ct).ConfigureAwait(false);
        return receipts.SelectMany(r => r.Lines)
            .Where(l => l.Type == ReceiptLineType.Sundry && l.AccountCode is not null)
            .GroupBy(l => l.AccountCode!)
            .OrderBy(g => g.Key)
            .Select(g => Row(new() { ["AccountCode"] = g.Key, ["AccountName"] = names.GetValueOrDefault(g.Key, string.Empty), ["Amount"] = g.Sum(l => l.Amount) }))
            .ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CollectionByTenderAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        (DateOnly from, DateOnly to) = Range(parameters);
        List<Receipt> receipts = await CommittedReceiptsAsync(companyId, from, to, ct).ConfigureAwait(false);
        return receipts.SelectMany(r => r.Tenders)
            .GroupBy(t => t.Type)
            .OrderBy(g => g.Key.ToString())
            .Select(g => Row(new() { ["Tender"] = g.Key.ToString(), ["Channel"] = Channel(g.Key), ["Amount"] = g.Sum(t => t.Amount) }))
            .ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CollectionByCurrencyAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        (DateOnly from, DateOnly to) = Range(parameters);
        List<Receipt> receipts = await CommittedReceiptsAsync(companyId, from, to, ct).ConfigureAwait(false);
        return receipts.SelectMany(r => r.Tenders)
            .GroupBy(t => t.Currency.ToString())
            .OrderBy(g => g.Key)
            .Select(g => Row(new() { ["Currency"] = g.Key, ["Transaction"] = g.Sum(t => t.Amount), ["Rate"] = string.Empty, ["BaseAmount"] = g.Sum(t => t.Amount) }))
            .ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CashierProductivityAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        (DateOnly from, DateOnly to) = Range(parameters);
        List<Receipt> receipts = await CommittedReceiptsAsync(companyId, from, to, ct).ConfigureAwait(false);
        Dictionary<long, int> variances = (await _db.Set<CashierSession>()
                .Where(s => s.CompanyId == companyId && s.CashUpVarianceBase != 0m)
                .Select(s => s.CashierUserId)
                .ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());

        return receipts.GroupBy(r => r.CashierUserId)
            .OrderBy(g => g.Key)
            .Select(g => Row(new() { ["Cashier"] = $"#{g.Key}", ["Receipts"] = g.Count(), ["Value"] = g.Sum(r => r.LineTotal), ["Variances"] = variances.GetValueOrDefault(g.Key) }))
            .ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GlReconciliationAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        (DateOnly from, DateOnly to) = Range(parameters);
        List<Receipt> receipts = await CommittedReceiptsAsync(companyId, from, to, ct).ConfigureAwait(false);
        decimal Sum(ReceiptLineType type) => receipts.SelectMany(r => r.Lines).Where(l => l.Type == type).Sum(l => l.Amount);
        return
        [
            Row(new() { ["Component"] = "Debtor payments (Cr debtor control)", ["Amount"] = Sum(ReceiptLineType.DebtorPayment) }),
            Row(new() { ["Component"] = "Sundry income", ["Amount"] = Sum(ReceiptLineType.Sundry) }),
            Row(new() { ["Component"] = "Deposits (liability)", ["Amount"] = Sum(ReceiptLineType.Deposit) }),
            Row(new() { ["Component"] = "Suspense (unallocated)", ["Amount"] = Sum(ReceiptLineType.Suspense) }),
        ];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TillVarianceAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        (DateOnly from, DateOnly to) = Range(parameters);
        List<CashierSession> sessions = await _db.Set<CashierSession>()
            .Where(s => s.CompanyId == companyId && s.CashUpVarianceBase != 0m && s.ClosedOn != null && s.ClosedOn >= from && s.ClosedOn <= to)
            .OrderBy(s => s.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        return sessions.Select(s => Row(new() { ["Cashier"] = $"#{s.CashierUserId}", ["Session"] = s.Id.ToString(), ["Variance"] = s.CashUpVarianceBase, ["Status"] = s.Status.ToString() })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CancelledReceiptsAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        (DateOnly from, DateOnly to) = Range(parameters);
        List<Receipt> receipts = await _db.Set<Receipt>().Include(r => r.Lines)
            .Where(r => r.CompanyId == companyId && r.Status == ReceiptStatus.Cancelled && r.Date >= from && r.Date <= to)
            .OrderBy(r => r.ReceiptNumber)
            .ToListAsync(ct).ConfigureAwait(false);
        return receipts.Select(r => Row(new() { ["ReceiptNumber"] = r.ReceiptNumber, ["Reason"] = string.Empty, ["Supervisor"] = string.Empty, ["Amount"] = r.LineTotal })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GapAnalysisAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        _ = parameters;
        var rows = await _db.Set<Receipt>()
            .Where(r => r.CompanyId == companyId)
            .Select(r => new { r.StationId, r.ReceiptNumber })
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> stationCodes = await _db.Set<CashierStation>()
            .Where(s => s.CompanyId == companyId)
            .ToDictionaryAsync(s => s.Id, s => s.Code, ct).ConfigureAwait(false);

        return rows.GroupBy(r => r.StationId)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                List<long> seqs = g.Select(r => Suffix(r.ReceiptNumber)).Where(n => n > 0).OrderBy(n => n).ToList();
                long min = seqs.Count > 0 ? seqs[0] : 0;
                long max = seqs.Count > 0 ? seqs[^1] : 0;
                long span = seqs.Count > 0 ? max - min + 1 : 0;
                long gaps = span - seqs.Count;
                return Row(new() { ["Station"] = stationCodes.GetValueOrDefault(g.Key, g.Key.ToString()), ["FromNumber"] = min, ["ToNumber"] = max, ["Gaps"] = gaps });
            })
            .ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RefundsRegisterAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly from = ReportParameters.TryDate(parameters, "FromDate") ?? DateOnly.MinValue;
        DateOnly to = ReportParameters.TryDate(parameters, "ToDate") ?? DateOnly.MaxValue;
        List<RefundRecord> refunds = await _db.Set<RefundRecord>()
            .Where(r => r.CompanyId == companyId && r.Date >= from && r.Date <= to)
            .OrderBy(r => r.Date).ThenBy(r => r.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        return refunds.Select(r => Row(new()
        {
            ["Payer"] = r.PayerName,
            ["Status"] = "Paid",
            ["Days"] = 0,
            ["Amount"] = r.Amount,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CashMovementsAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly from = ReportParameters.TryDate(parameters, "FromDate") ?? DateOnly.MinValue;
        DateOnly to = ReportParameters.TryDate(parameters, "ToDate") ?? DateOnly.MaxValue;
        List<SessionCashMovement> movements = await _db.Set<SessionCashMovement>()
            .Where(m => m.CompanyId == companyId && m.Date >= from && m.Date <= to)
            .OrderBy(m => m.Date).ThenBy(m => m.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        return movements.Select(m => Row(new()
        {
            ["When"] = m.Date.ToDateTime(TimeOnly.MinValue),
            ["Type"] = m.Type.ToString(),
            ["Reason"] = m.Reason,
            ["Amount"] = m.Amount,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DailyBankingSummaryAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly date = ReportParameters.Date(parameters, "Date");
        List<BankDeposit> deposits = await _db.Set<BankDeposit>()
            .Where(d => d.CompanyId == companyId && d.DepositDate == date)
            .ToListAsync(ct).ConfigureAwait(false);
        return deposits.GroupBy(d => d.Status)
            .OrderBy(g => g.Key.ToString())
            .Select(g => Row(new() { ["Stage"] = g.Key.ToString(), ["Amount"] = g.Sum(d => d.Amount) }))
            .ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CashInTransitAgeingAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly asAt = ReportParameters.Date(parameters, "AsAtDate");
        List<BankDeposit> deposits = await _db.Set<BankDeposit>()
            .Where(d => d.CompanyId == companyId && d.Status == BankDepositStatus.InTransit)
            .OrderBy(d => d.DepositDate)
            .ToListAsync(ct).ConfigureAwait(false);
        return deposits.Select(d => Row(new() { ["Reference"] = d.Reference, ["DepositDate"] = d.DepositDate.ToDateTime(TimeOnly.MinValue), ["Days"] = Math.Max(0, asAt.DayNumber - d.DepositDate.DayNumber), ["Amount"] = d.Amount })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ImportExceptionsAsync(long companyId, CancellationToken ct)
    {
        var lines = await _db.Set<ImportBatch>()
            .Where(b => b.CompanyId == companyId)
            .SelectMany(b => b.Lines)
            .Where(l => l.Status == ImportLineStatus.Unmatched)
            .Select(l => new { l.PayerReference, l.PayerName, l.Amount })
            .ToListAsync(ct).ConfigureAwait(false);
        return lines.Select(l => Row(new() { ["PayerReference"] = l.PayerReference, ["PayerName"] = l.PayerName, ["Reason"] = "No account matched", ["Amount"] = l.Amount })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> PaymentHistoryAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        long account = ReportParameters.Long(parameters, "Account");
        List<Receipt> receipts = await _db.Set<Receipt>().Include(r => r.Lines)
            .Where(r => r.CompanyId == companyId && r.PayerAccountId == account)
            .OrderBy(r => r.Date).ThenBy(r => r.ReceiptNumber)
            .ToListAsync(ct).ConfigureAwait(false);
        return receipts.Select(r => Row(new() { ["Date"] = r.Date.ToDateTime(TimeOnly.MinValue), ["ReceiptNumber"] = r.ReceiptNumber, ["Type"] = r.Status.ToString(), ["Amount"] = r.LineTotal })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CreditBalancesAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly asAt = ReportParameters.Date(parameters, "AsAtDate");
        var credits = await _db.Set<Core.Trading.OpenItem>()
            .Where(i => i.CompanyId == companyId && i.Ledger == Core.Trading.TradingLedger.AccountsReceivable && i.Sign == Core.Trading.LedgerSign.Credit && i.OutstandingTransaction > 0m)
            .Select(i => new { i.TraderId, i.OutstandingTransaction, i.DocumentDate })
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, (string Code, string Name)> customers = await _db.Set<Core.Trading.Customer>()
            .Where(c => c.CompanyId == companyId)
            .ToDictionaryAsync(c => c.Id, c => (c.Code, c.Name), ct).ConfigureAwait(false);

        return credits.GroupBy(c => c.TraderId)
            .Select(g =>
            {
                (string code, string name) = customers.GetValueOrDefault(g.Key, (g.Key.ToString(), string.Empty));
                long days = Math.Max(0, asAt.DayNumber - g.Min(x => x.DocumentDate).DayNumber);
                return Row(new() { ["Account"] = code, ["Name"] = name, ["Days"] = days, ["Credit"] = g.Sum(x => x.OutstandingTransaction) });
            })
            .ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TopPayersAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        (DateOnly from, DateOnly to) = Range(parameters);
        List<Receipt> receipts = await CommittedReceiptsAsync(companyId, from, to, ct).ConfigureAwait(false);
        return receipts.GroupBy(r => new { r.PayerKind, r.PayerAccountId })
            .Select(g => Row(new() { ["Payer"] = g.Key.PayerKind == PayerKind.Anonymous ? "(sundry)" : $"{g.Key.PayerKind} #{g.Key.PayerAccountId}", ["Count"] = g.Count(), ["Amount"] = g.Sum(r => r.LineTotal) }))
            .OrderByDescending(r => (decimal)r["Amount"]!)
            .Take(20)
            .ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AgentAgeingAsync(long companyId, CancellationToken ct)
    {
        List<Agent> agents = await _db.Set<Agent>()
            .Where(a => a.CompanyId == companyId)
            .OrderBy(a => a.Code)
            .ToListAsync(ct).ConfigureAwait(false);
        return agents.Where(a => a.Outstanding > 0m)
            .Select(a => Row(new() { ["Agent"] = $"{a.Code} — {a.Name}", ["Days"] = 0, ["Outstanding"] = a.Outstanding }))
            .ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CollectionTrendAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        (DateOnly from, DateOnly to) = Range(parameters);
        List<Receipt> receipts = await CommittedReceiptsAsync(companyId, from, to, ct).ConfigureAwait(false);
        return receipts.GroupBy(r => $"{r.Date.Year:D4}-{r.Date.Month:D2}")
            .OrderBy(g => g.Key)
            .Select(g => Row(new() { ["Period"] = g.Key, ["Amount"] = g.Sum(r => r.LineTotal) }))
            .ToList();
    }

    private async Task<Dictionary<string, string>> AccountNamesAsync(long companyId, CancellationToken ct) =>
        await _db.Accounts.Where(a => a.CompanyId == companyId).ToDictionaryAsync(a => a.AccountCode, a => a.Name, ct).ConfigureAwait(false);

    private static (DateOnly From, DateOnly To) Range(IReadOnlyDictionary<string, object?> parameters) =>
        (ReportParameters.TryDate(parameters, "FromDate") ?? DateOnly.MinValue, ReportParameters.TryDate(parameters, "ToDate") ?? DateOnly.MaxValue);

    private static string Channel(TenderType type) => type switch
    {
        TenderType.Cash => "Counter",
        TenderType.Card => "Card terminal",
        TenderType.MobileMoney => "Mobile money",
        TenderType.BankTransfer => "Bank",
        TenderType.Cheque => "Cheque",
        _ => "Other",
    };

    private static long Suffix(string receiptNumber)
    {
        int dash = receiptNumber.LastIndexOf('-');
        string tail = dash >= 0 ? receiptNumber[(dash + 1)..] : receiptNumber;
        return long.TryParse(tail, out long value) ? value : 0;
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
