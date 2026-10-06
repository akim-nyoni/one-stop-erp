using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.MasterData;
using One4All.Application.Abstractions.Onboarding;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Onboarding;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Onboarding;

/// <summary>Imports master data and opening balances from CSV templates, validate-first and all-or-nothing (brief §10).</summary>
public sealed class OpeningBalanceService : IOpeningBalanceService
{
    private const string TrialBalanceDocumentType = "OpeningTrialBalance";

    private readonly One4AllDbContext _db;
    private readonly IMasterDataService _master;
    private readonly IInventoryService _inventory;
    private readonly ISalesLedgerService _receivables;
    private readonly IPurchaseLedgerService _payables;
    private readonly IPostingEngine _engine;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="master">Creates traders and items.</param>
    /// <param name="inventory">Receives opening stock.</param>
    /// <param name="receivables">Posts open receivable items.</param>
    /// <param name="payables">Posts open payable items.</param>
    /// <param name="engine">Posts the opening trial balance.</param>
    public OpeningBalanceService(One4AllDbContext db, IMasterDataService master, IInventoryService inventory, ISalesLedgerService receivables, IPurchaseLedgerService payables, IPostingEngine engine)
    {
        _db = db;
        _master = master;
        _inventory = inventory;
        _receivables = receivables;
        _payables = payables;
        _engine = engine;
    }

    /// <inheritdoc />
    public async Task<ImportReport> ImportCustomersAsync(long companyId, string csv, bool commit, CancellationToken ct = default)
    {
        HashSet<string> existing = [.. await _db.Customers.Where(c => c.CompanyId == companyId).Select(c => c.Code).ToListAsync(ct).ConfigureAwait(false)];
        return await TradersAsync(csv, existing, commit, ct, (code, name, currency, row) =>
            _master.CreateCustomerAsync(companyId, code, name, currency, row.Number("credit_limit") ?? 0m, ct)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ImportReport> ImportSuppliersAsync(long companyId, string csv, bool commit, CancellationToken ct = default)
    {
        HashSet<string> existing = [.. await _db.Suppliers.Where(s => s.CompanyId == companyId).Select(s => s.Code).ToListAsync(ct).ConfigureAwait(false)];
        return await TradersAsync(csv, existing, commit, ct, (code, name, currency, _) => _master.CreateSupplierAsync(companyId, code, name, currency, ct)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ImportReport> ImportItemsAsync(long companyId, string csv, bool commit, CancellationToken ct = default)
    {
        (IReadOnlyList<CsvRow> rows, IReadOnlyList<string> missing) = CsvTable.Read(csv, "code", "description");
        if (missing.Count > 0)
        {
            return Missing(missing);
        }

        HashSet<string> existing = [.. await _db.Items.Where(i => i.CompanyId == companyId).Select(i => i.Code).ToListAsync(ct).ConfigureAwait(false)];
        HashSet<string> accounts = [.. await _db.Accounts.Where(a => a.CompanyId == companyId).Select(a => a.AccountCode).ToListAsync(ct).ConfigureAwait(false)];
        List<ImportError> errors = [];
        List<(NewItem Item, string? Category)> toCreate = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        int skipped = 0;
        foreach (CsvRow row in rows)
        {
            string? code = row.Text("code")?.ToUpperInvariant();
            string? description = row.Text("description");
            ItemType type = ItemType.Stock;
            if (code is null || description is null || (row.Text("type") is string t && !Enum.TryParse(t, true, out type)))
            {
                errors.Add(new ImportError(row.LineNumber, "An item needs a code, a description and, if given, a known type."));
                continue;
            }

            if (!seen.Add(code))
            {
                errors.Add(new ImportError(row.LineNumber, $"Item {code} appears twice."));
                continue;
            }

            if (existing.Contains(code))
            {
                skipped++;
                continue;
            }

            string? revenue = row.Text("revenue_account")?.ToUpperInvariant(), cost = row.Text("cost_account")?.ToUpperInvariant();
            if ((revenue is not null && !accounts.Contains(revenue)) || (cost is not null && !accounts.Contains(cost)))
            {
                errors.Add(new ImportError(row.LineNumber, $"Item {code} names an account that does not exist."));
                continue;
            }

            toCreate.Add((new NewItem(companyId, code, description, type, revenue, cost), row.Text("category")));
        }

        return await CommitAsync(rows.Count, skipped, errors, commit, ct, async () =>
        {
            foreach ((NewItem item, string? category) in toCreate)
            {
                Result<long> created = await _master.CreateItemAsync(item, ct).ConfigureAwait(false);
                if (created.IsFailure)
                {
                    return created.Error;
                }

                if (category is not null)
                {
                    (await _db.Items.SingleAsync(i => i.Id == created.Value, ct).ConfigureAwait(false)).Category = category;
                    await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                }
            }

            return toCreate.Count;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ImportReport> ImportStockAsync(long companyId, string csv, DateOnly date, string suspenseAccountCode, bool commit, CancellationToken ct = default)
    {
        (IReadOnlyList<CsvRow> rows, IReadOnlyList<string> missing) = CsvTable.Read(csv, "item", "warehouse", "quantity", "unit_cost");
        if (missing.Count > 0)
        {
            return Missing(missing);
        }

        List<ImportError> errors = [];
        if (await SuspenseMissingAsync(companyId, suspenseAccountCode, ct).ConfigureAwait(false) is string problem)
        {
            errors.Add(new ImportError(0, problem));
        }

        Dictionary<string, long> items = await _db.Items.Where(i => i.CompanyId == companyId).ToDictionaryAsync(i => i.Code, i => i.Id, ct).ConfigureAwait(false);
        Dictionary<string, long> warehouses = await _db.Warehouses.Where(w => w.CompanyId == companyId).ToDictionaryAsync(w => w.Code, w => w.Id, ct).ConfigureAwait(false);
        List<ReceiveStockRequest> receipts = [];
        foreach (CsvRow row in rows)
        {
            try
            {
                string itemCode = row.Text("item")?.ToUpperInvariant() ?? string.Empty, warehouseCode = row.Text("warehouse")?.ToUpperInvariant() ?? string.Empty;
                decimal quantity = row.Number("quantity") ?? 0m, cost = row.Number("unit_cost") ?? -1m;
                if (!items.TryGetValue(itemCode, out long itemId) || !warehouses.TryGetValue(warehouseCode, out long warehouseId) || quantity <= 0m || cost < 0m)
                {
                    errors.Add(new ImportError(row.LineNumber, "Stock needs a known item and warehouse, a positive quantity and a unit cost that is not negative."));
                    continue;
                }

                receipts.Add(new ReceiveStockRequest
                {
                    CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = date, Quantity = quantity, UnitCost = cost, CreditAccountCode = suspenseAccountCode.Trim().ToUpperInvariant(),
                    Reference = "OPENING",
                });
            }
            catch (FormatException ex)
            {
                errors.Add(new ImportError(row.LineNumber, ex.Message));
            }
        }

        return await CommitAsync(rows.Count, 0, errors, commit, ct, async () =>
        {
            foreach (ReceiveStockRequest receipt in receipts)
            {
                Result<StockMovementResult> received = await _inventory.ReceiveAsync(receipt, ct).ConfigureAwait(false);
                if (received.IsFailure)
                {
                    return received.Error;
                }
            }

            return receipts.Count;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ImportReport> ImportOpenItemsAsync(long companyId, TradingLedger ledger, string csv, string suspenseAccountCode, bool commit, CancellationToken ct = default)
    {
        (IReadOnlyList<CsvRow> rows, IReadOnlyList<string> missing) = CsvTable.Read(csv, "trader", "document", "date", "amount");
        if (missing.Count > 0)
        {
            return Missing(missing);
        }

        List<ImportError> errors = [];
        if (await SuspenseMissingAsync(companyId, suspenseAccountCode, ct).ConfigureAwait(false) is string problem)
        {
            errors.Add(new ImportError(0, problem));
        }

        bool receivable = ledger == TradingLedger.AccountsReceivable;
        Dictionary<string, (long Id, CurrencyCode Currency)> traders = receivable
            ? await _db.Customers.Where(c => c.CompanyId == companyId).ToDictionaryAsync(c => c.Code, c => (c.Id, c.Currency), ct).ConfigureAwait(false)
            : await _db.Suppliers.Where(s => s.CompanyId == companyId).ToDictionaryAsync(s => s.Code, s => (s.Id, s.Currency), ct).ConfigureAwait(false);
        HashSet<string> onLedger = [.. await _db.OpenItems.Where(o => o.CompanyId == companyId && o.Ledger == ledger).Select(o => o.DocumentNumber).ToListAsync(ct).ConfigureAwait(false)];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<(long Trader, CurrencyCode Currency, string Document, DateOnly Date, decimal Amount)> items = [];
        int skipped = 0;
        foreach (CsvRow row in rows)
        {
            try
            {
                string code = row.Text("trader")?.ToUpperInvariant() ?? string.Empty, document = row.Text("document") ?? string.Empty;
                DateOnly? date = row.Date("date");
                decimal amount = row.Number("amount") ?? 0m;
                if (!traders.TryGetValue(code, out var trader) || document.Length == 0 || date is null || amount == 0m)
                {
                    errors.Add(new ImportError(row.LineNumber, "An open item needs a known trader, a document number, a date and a non-zero amount."));
                    continue;
                }

                if (!seen.Add(document))
                {
                    errors.Add(new ImportError(row.LineNumber, $"Document {document} appears twice."));
                    continue;
                }

                if (onLedger.Contains(document))
                {
                    skipped++;
                    continue;
                }

                items.Add((trader.Id, trader.Currency, document, date.Value, amount));
            }
            catch (FormatException ex)
            {
                errors.Add(new ImportError(row.LineNumber, ex.Message));
            }
        }

        string suspense = suspenseAccountCode.Trim().ToUpperInvariant();
        return await CommitAsync(rows.Count, skipped, errors, commit, ct, async () =>
        {
            foreach ((long trader, CurrencyCode currency, string document, DateOnly date, decimal amount) in items)
            {
                Result<TradingPostingResult> posted = (receivable, amount > 0m) switch
                {
                    (true, true) => await _receivables.PostInvoiceAsync(new SalesInvoiceRequest
                    {
                        CompanyId = companyId, CustomerId = trader, DocumentNumber = document, DocumentDate = date, Currency = currency, NetAmount = amount, SalesAccountCode = suspense,
                        Narrative = "Opening balance",
                    }, ct).ConfigureAwait(false),
                    (true, false) => await _receivables.PostCreditNoteAsync(new SalesCreditNoteRequest
                    {
                        CompanyId = companyId, CustomerId = trader, DocumentNumber = document, DocumentDate = date, Currency = currency, NetAmount = -amount, SalesAccountCode = suspense,
                    }, ct).ConfigureAwait(false),
                    (false, true) => await _payables.PostInvoiceAsync(new PurchaseInvoiceRequest
                    {
                        CompanyId = companyId, SupplierId = trader, DocumentNumber = document, DocumentDate = date, Currency = currency, NetAmount = amount, ExpenseAccountCode = suspense,
                        Narrative = "Opening balance",
                    }, ct).ConfigureAwait(false),
                    (false, false) => await _payables.PostCreditNoteAsync(new PurchaseCreditNoteRequest
                    {
                        CompanyId = companyId, SupplierId = trader, DocumentNumber = document, DocumentDate = date, Currency = currency, NetAmount = -amount, ExpenseAccountCode = suspense,
                    }, ct).ConfigureAwait(false),
                };
                if (posted.IsFailure)
                {
                    return posted.Error;
                }
            }

            return items.Count;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ImportReport> ImportTrialBalanceAsync(long companyId, string csv, DateOnly date, string suspenseAccountCode, bool commit, CancellationToken ct = default)
    {
        (IReadOnlyList<CsvRow> rows, IReadOnlyList<string> missing) = CsvTable.Read(csv, "account", "debit", "credit");
        if (missing.Count > 0)
        {
            return Missing(missing);
        }

        List<ImportError> errors = [];
        if (await SuspenseMissingAsync(companyId, suspenseAccountCode, ct).ConfigureAwait(false) is string problem)
        {
            errors.Add(new ImportError(0, problem));
        }

        if (await _db.Journals.AnyAsync(j => j.CompanyId == companyId && j.DocumentType == TrialBalanceDocumentType, ct).ConfigureAwait(false))
        {
            errors.Add(new ImportError(0, "The opening trial balance has already been loaded (Migration.TrialBalanceLoaded)."));
        }

        Dictionary<string, ControlAccountType> accounts = await _db.Accounts.Where(a => a.CompanyId == companyId).ToDictionaryAsync(a => a.AccountCode, a => a.ControlAccountType, ct).ConfigureAwait(false);
        Dictionary<string, decimal> net = new(StringComparer.Ordinal);
        decimal debits = 0m, credits = 0m;
        foreach (CsvRow row in rows)
        {
            try
            {
                string account = row.Text("account")?.ToUpperInvariant() ?? string.Empty;
                decimal debit = row.Number("debit") ?? 0m, credit = row.Number("credit") ?? 0m;
                if (!accounts.ContainsKey(account) || debit < 0m || credit < 0m)
                {
                    errors.Add(new ImportError(row.LineNumber, $"Account {account} does not exist, or a debit or credit is negative."));
                    continue;
                }

                debits += debit;
                credits += credit;
                net[account] = net.GetValueOrDefault(account) + debit - credit;
            }
            catch (FormatException ex)
            {
                errors.Add(new ImportError(row.LineNumber, ex.Message));
            }
        }

        if (debits != credits)
        {
            errors.Add(new ImportError(0, $"The trial balance does not balance: debits {debits:0.00}, credits {credits:0.00}."));
        }

        // Control accounts come in through their sub-ledgers; the suspense account takes their trial balance figures instead.
        string suspense = suspenseAccountCode.Trim().ToUpperInvariant();
        List<JournalLineRequest> lines = [.. net.Where(n => n.Value != 0m && accounts[n.Key] == ControlAccountType.None)
            .Select(n => new JournalLineRequest { AccountCode = n.Key, Side = n.Value > 0m ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = Math.Abs(n.Value) })];
        decimal toSuspense = -lines.Sum(l => l.Side == JournalSide.Debit ? l.TransactionAmount : -l.TransactionAmount);
        if (toSuspense != 0m)
        {
            lines.Add(new JournalLineRequest { AccountCode = suspense, Side = toSuspense > 0m ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = Math.Abs(toSuspense), Narrative = "Control accounts per the trial balance" });
        }

        return await CommitAsync(rows.Count, 0, errors, commit, ct, async () =>
        {
            if (lines.Count == 0)
            {
                return 0;
            }

            CurrencyCode currency = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.BaseCurrency).SingleAsync(ct).ConfigureAwait(false);
            Result<PostingReceipt> posted = await _engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = currency, EnforceManualJournalRules = false,
                DocumentType = TrialBalanceDocumentType, DocumentNumber = "OPENING-TB", Narrative = "Opening trial balance", Lines = lines,
            }, ct).ConfigureAwait(false);
            return posted.IsFailure ? posted.Error : 1;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<OpeningBalanceReconciliation>> ReconcileAsync(long companyId, string suspenseAccountCode, CancellationToken ct = default)
    {
        if (await SuspenseMissingAsync(companyId, suspenseAccountCode, ct).ConfigureAwait(false) is string problem)
        {
            return Error.NotFound("Migration.SuspenseNotFound", problem);
        }

        string suspense = suspenseAccountCode.Trim().ToUpperInvariant();
        Dictionary<string, ControlAccountType> controls = await _db.Accounts.Where(a => a.CompanyId == companyId && a.ControlAccountType != ControlAccountType.None)
            .ToDictionaryAsync(a => a.AccountCode, a => a.ControlAccountType, ct).ConfigureAwait(false);
        var balances = await _db.Journals.Where(j => j.CompanyId == companyId && j.Status == JournalStatus.Posted).SelectMany(j => j.Lines)
            .GroupBy(l => l.AccountCode).Select(g => new { Account = g.Key, Balance = g.Sum(l => l.BaseDebit - l.BaseCredit) }).ToListAsync(ct).ConfigureAwait(false);
        decimal Balance(ControlAccountType type) => balances.Where(b => controls.GetValueOrDefault(b.Account) == type).Sum(b => b.Balance);
        async Task<decimal> OutstandingAsync(TradingLedger ledger) => await _db.OpenItems.Where(o => o.CompanyId == companyId && o.Ledger == ledger)
            .SumAsync(o => o.Sign == LedgerSign.Debit ? o.OutstandingBase : -o.OutstandingBase, ct).ConfigureAwait(false);

        decimal stock = await _db.StockBalances.Where(b => b.CompanyId == companyId).SumAsync(b => (decimal?)b.TotalValue, ct).ConfigureAwait(false) ?? 0m;
        List<ControlCheck> checks =
        [
            new("Receivables", Balance(ControlAccountType.AccountsReceivable), await OutstandingAsync(TradingLedger.AccountsReceivable).ConfigureAwait(false)),
            new("Payables", -Balance(ControlAccountType.AccountsPayable), -await OutstandingAsync(TradingLedger.AccountsPayable).ConfigureAwait(false)),
            new("Stock", Balance(ControlAccountType.Stock), stock),
        ];
        bool loaded = await _db.Journals.AnyAsync(j => j.CompanyId == companyId && j.DocumentType == TrialBalanceDocumentType, ct).ConfigureAwait(false);
        return new OpeningBalanceReconciliation(suspense, balances.Where(b => b.Account == suspense).Sum(b => b.Balance), checks, loaded);
    }

    private static ImportReport Missing(IReadOnlyList<string> columns) => new(0, 0, 0, [new ImportError(1, $"Missing columns: {string.Join(", ", columns)}.")], false);

    private async Task<ImportReport> TradersAsync(string csv, HashSet<string> existing, bool commit, CancellationToken ct, Func<string, string, CurrencyCode, CsvRow, Task<Result<long>>> create)
    {
        (IReadOnlyList<CsvRow> rows, IReadOnlyList<string> missing) = CsvTable.Read(csv, "code", "name", "currency");
        if (missing.Count > 0)
        {
            return Missing(missing);
        }

        List<ImportError> errors = [];
        List<(string Code, string Name, CurrencyCode Currency, CsvRow Row)> toCreate = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        int skipped = 0;
        foreach (CsvRow row in rows)
        {
            try
            {
                string? code = row.Text("code")?.ToUpperInvariant(), name = row.Text("name");
                if (code is null || name is null || row.Text("currency") is not string currency || currency.Length != 3 || row.Number("credit_limit") < 0m)
                {
                    errors.Add(new ImportError(row.LineNumber, "A trader needs a code, a name, a three-letter currency and any credit limit not negative."));
                    continue;
                }

                if (!seen.Add(code))
                {
                    errors.Add(new ImportError(row.LineNumber, $"{code} appears twice."));
                    continue;
                }

                if (existing.Contains(code))
                {
                    skipped++;
                    continue;
                }

                toCreate.Add((code, name, CurrencyCode.Parse(currency.ToUpperInvariant()), row));
            }
            catch (FormatException ex)
            {
                errors.Add(new ImportError(row.LineNumber, ex.Message));
            }
        }

        return await CommitAsync(rows.Count, skipped, errors, commit, ct, async () =>
        {
            foreach ((string code, string name, CurrencyCode currency, CsvRow row) in toCreate)
            {
                Result<long> created = await create(code, name, currency, row).ConfigureAwait(false);
                if (created.IsFailure)
                {
                    return created.Error;
                }
            }

            return toCreate.Count;
        }).ConfigureAwait(false);
    }

    // Writes only when asked and the file is clean, all in one unit of work; a failure part-way rolls everything back.
    private async Task<ImportReport> CommitAsync(int rows, int skipped, List<ImportError> errors, bool commit, CancellationToken ct, Func<Task<Result<int>>> write)
    {
        if (errors.Count > 0 || !commit)
        {
            return new ImportReport(rows, 0, skipped, errors, false);
        }

        Result<int> written = await _db.AtomicAsync(write, ct).ConfigureAwait(false);
        return written.IsSuccess
            ? new ImportReport(rows, written.Value, skipped, [], true)
            : new ImportReport(rows, 0, skipped, [new ImportError(0, written.Error.Message)], false);
    }

    private async Task<string?> SuspenseMissingAsync(long companyId, string suspenseAccountCode, CancellationToken ct)
    {
        string code = (suspenseAccountCode ?? string.Empty).Trim().ToUpperInvariant();
        return await _db.Accounts.AnyAsync(a => a.CompanyId == companyId && a.AccountCode == code, ct).ConfigureAwait(false) ? null : $"Suspense account {code} does not exist.";
    }
}
