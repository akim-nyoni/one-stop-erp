using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Security;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Api;
using One4All.Core.Common;
using One4All.Core.Pos;
using One4All.Core.Receipting;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using ReceiptRequest = One4All.Application.Abstractions.Receipting.ReceiptRequest;
using static One4All.Server.TradingApiEndpoints;

namespace One4All.Server;

/// <summary>
/// The REST API's ledger, receipting, bank statement and till endpoints (brief §11): the trial balance, chart of accounts and aged debtors and
/// creditors; cashier receipts (post, reverse, find a payer); posting a bank statement line as a customer receipt, supplier payment or straight to an
/// account; and the till sale export, void and X-read. Documents name accounts, customers, suppliers, bank accounts and tills by code; each step runs
/// through the same application service the desktop uses. A till named by code is held to the user's till scope. Closing a till (the Z-read) is not
/// offered: it needs the physical cash count.
/// </summary>
internal static class LedgerApiEndpoints
{
    private const int PageSize = 200;

    /// <summary>Maps the endpoints onto the API group.</summary>
    /// <param name="api">The API route group (authorisation is required on every route).</param>
    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        MapLedgers(api);
        MapReceipting(api);
        MapBankStatementLines(api);
        MapTills(api);
    }

    private static void MapLedgers(RouteGroupBuilder api)
    {
        // A posted manual (GL) journal reversed by its exact inverse; it needs the journal reverse right, and sub-ledger journals go through their documents.
        api.MapPost("/api/gl/journals/{number}/reverse", async (string number, long companyId, long userId, DateOnly date, string reason, One4AllDbContext db,
            IPermissionService permissions, Application.Abstractions.Accounting.IPostingEngine engine, CancellationToken ct) =>
        {
            if (!(await permissions.ResolveAsync(userId, ct).ConfigureAwait(false)).Has("GeneralLedger.Journal", OperationFlags.VoidReverse))
            {
                return Value(Result<bool>.Failure(Error.Forbidden("Api.JournalReverse", "Reversing a journal needs the journal reverse right (GeneralLedger.Journal).")));
            }

            string key = (number ?? string.Empty).Trim();
            var journal = await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && j.JournalNumber == key).Select(j => new { j.Id, j.SourceModule }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (journal is null)
            {
                return NotFound($"No journal {key}.");
            }

            return journal.SourceModule != "GL"
                ? Value(Result<bool>.Failure(Error.Rule("Api.SubLedgerJournal", $"Journal {key} came from {journal.SourceModule}; reverse it through its own document.")))
                : Value(await engine.ReverseAsync(companyId, journal.Id, date, reason, ct).ConfigureAwait(false));
        }).RequireAuthorization().WithName("ApiReverseJournal");

        api.MapGet("/api/gl/trial-balance", async (long companyId, DateOnly? asAt, DateOnly? from, DateOnly? to, IGeneralLedgerReports reports, CancellationToken ct) =>
        {
            if (from is DateOnly start)
            {
                if (asAt is not null)
                {
                    return BadRequest("Give asAt, or from and to, not both.");
                }

                DateOnly end = to ?? Today();
                return end < start
                    ? BadRequest("The range ends before it starts.")
                    : Results.Ok(LedgerApiMapping.TrialBalanceMovement(await reports.TrialBalanceMovementAsync(companyId, start, end, ct).ConfigureAwait(false)));
            }

            return Results.Ok(LedgerApiMapping.TrialBalance(await reports.TrialBalanceAsync(companyId, asAt ?? to ?? Today(), ct).ConfigureAwait(false)));
        }).RequireAuthorization().WithName("ApiTrialBalance");

        api.MapGet("/api/gl/accounts", async (long companyId, string? search, One4AllDbContext db, CancellationToken ct) =>
        {
            IQueryable<Core.Accounting.LedgerAccount> query = db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId);
            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim().ToUpperInvariant();
                query = query.Where(a => a.AccountCode.ToUpper().Contains(term) || a.Name.ToUpper().Contains(term));
            }

            List<Core.Accounting.LedgerAccount> accounts = await query.OrderBy(a => a.AccountCode).ToListAsync(ct).ConfigureAwait(false);
            return Results.Ok(accounts.Select(LedgerApiMapping.Account).ToList());
        }).RequireAuthorization().WithName("ApiChartOfAccounts");

        api.MapGet("/api/ar/aged-debtors", async (long companyId, DateOnly? asAt, ITradingReports reports, CancellationToken ct) =>
            Results.Ok(LedgerApiMapping.AgedBalances(await reports.AgedDebtorsAsync(companyId, asAt ?? Today(), ct).ConfigureAwait(false))))
        .RequireAuthorization().WithName("ApiAgedDebtors");

        api.MapGet("/api/ap/aged-creditors", async (long companyId, DateOnly? asAt, ITradingReports reports, CancellationToken ct) =>
            Results.Ok(LedgerApiMapping.AgedBalances(await reports.AgedCreditorsAsync(companyId, asAt ?? Today(), ct).ConfigureAwait(false))))
        .RequireAuthorization().WithName("ApiAgedCreditors");
    }

    private static void MapReceipting(RouteGroupBuilder api)
    {
        api.MapPost("/api/receipting/receipts", async (long companyId, long userId, CashierReceiptApi request, One4AllDbContext db, IReceiptingService receipting, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            long? payer = null;
            if (!string.IsNullOrWhiteSpace(request.PayerCode))
            {
                string key = OrdersApiMapping.Key(request.PayerCode);
                payer = await db.Customers.AsNoTracking().Where(c => c.CompanyId == companyId && c.Code.ToUpper() == key).Select(c => (long?)c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false)
                    ?? await db.Properties.AsNoTracking().Where(p => p.CompanyId == companyId && p.StandNumber == key).Select(p => (long?)p.RatepayerCustomerId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                if (payer is null)
                {
                    return NotFound($"No customer or stand {key}.");
                }
            }

            // The signed-in cashier's open session (at the station named, else their latest).
            string? station = string.IsNullOrWhiteSpace(request.StationCode) ? null : OrdersApiMapping.Key(request.StationCode);
            long? session = await (from s in db.CashierSessions.AsNoTracking()
                                   join st in db.CashierStations.AsNoTracking() on s.StationId equals st.Id
                                   where s.CompanyId == companyId && s.CashierUserId == userId && s.Status == SessionStatus.Open && (station == null || st.Code.ToUpper() == station)
                                   orderby s.Id descending
                                   select (long?)s.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (session is null)
            {
                return Value(Result<bool>.Failure(Error.Rule("Api.NoSession", station is null ? "You have no open cashier session: open one first." : $"You have no open cashier session at station {station}.")));
            }

            Result<ReceiptRequest> mapped = LedgerApiMapping.Receipt(companyId, session.Value, request, payer);
            return mapped.IsFailure ? Value(mapped) : Value(await receipting.PostReceiptAsync(mapped.Value, ct).ConfigureAwait(false));
        }).RequireAuthorization().WithName("ApiPostCashierReceipt");

        api.MapPost("/api/receipting/receipts/{number}/reverse", async (string number, long companyId, long userId, ReceiptReversalApi request, One4AllDbContext db, IReceiptingService receipting,
            CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            string key = OrdersApiMapping.Key(number);
            long? receipt = await db.Receipts.AsNoTracking().Where(r => r.CompanyId == companyId && r.ReceiptNumber.ToUpper() == key).Select(r => (long?)r.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (receipt is null)
            {
                return NotFound($"No receipt {key}.");
            }

            Result<string> reason = LedgerApiMapping.Reason(request.Reason);
            return reason.IsFailure ? Value(reason) : Value(await receipting.ReverseReceiptAsync(companyId, receipt.Value, request.Date, userId, reason.Value, ct).ConfigureAwait(false));
        }).RequireAuthorization().WithName("ApiReverseCashierReceipt");

        api.MapGet("/api/receipting/payers", async (long companyId, string? search, int? max, IPayerSearchService payers, CancellationToken ct) =>
        {
            string text = (search ?? string.Empty).Trim();
            if (text.Length < 2)
            {
                return BadRequest("Search with at least two characters.");
            }

            IReadOnlyList<PayerHit> hits = await payers.SearchAsync(companyId, text, Math.Clamp(max ?? 20, 1, 100), ct).ConfigureAwait(false);
            return Results.Ok(hits.Select(LedgerApiMapping.Payer).ToList());
        }).RequireAuthorization().WithName("ApiSearchPayers");
    }

    private static void MapBankStatementLines(RouteGroupBuilder api)
    {
        api.MapPost("/api/banking/statement-lines/{lineId:long}/customer-receipt", async (long lineId, long companyId, StatementLineReceiptApi request, One4AllDbContext db,
            IBankStatementPostingService posting, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            long? bank = await BankAccountAsync(db, companyId, request.BankAccountCode, ct).ConfigureAwait(false);
            if (bank is null)
            {
                return NotFound($"No bank account {OrdersApiMapping.Key(request.BankAccountCode)}.");
            }

            string key = OrdersApiMapping.Key(request.CustomerCode);
            long? customer = await db.Customers.AsNoTracking().Where(c => c.CompanyId == companyId && c.Code.ToUpper() == key).Select(c => (long?)c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            return customer is null
                ? NotFound($"No customer {key}.")
                : Value(await posting.PostCustomerReceiptAsync(companyId, bank.Value, lineId, customer.Value, Clean(request.DocumentNumber), ct).ConfigureAwait(false));
        }).RequireAuthorization().WithName("ApiPostStatementLineReceipt");

        api.MapPost("/api/banking/statement-lines/{lineId:long}/supplier-payment", async (long lineId, long companyId, StatementLinePaymentApi request, One4AllDbContext db,
            IBankStatementPostingService posting, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            long? bank = await BankAccountAsync(db, companyId, request.BankAccountCode, ct).ConfigureAwait(false);
            if (bank is null)
            {
                return NotFound($"No bank account {OrdersApiMapping.Key(request.BankAccountCode)}.");
            }

            string key = OrdersApiMapping.Key(request.SupplierCode);
            long? supplier = await db.Suppliers.AsNoTracking().Where(s => s.CompanyId == companyId && s.Code.ToUpper() == key).Select(s => (long?)s.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            return supplier is null
                ? NotFound($"No supplier {key}.")
                : Value(await posting.PostSupplierPaymentAsync(companyId, bank.Value, lineId, supplier.Value, Clean(request.DocumentNumber), ct).ConfigureAwait(false));
        }).RequireAuthorization().WithName("ApiPostStatementLinePayment");

        api.MapPost("/api/banking/statement-lines/{lineId:long}/to-account", async (long lineId, long companyId, StatementLineToAccountApi request, One4AllDbContext db,
            IBankStatementPostingService posting, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            long? bank = await BankAccountAsync(db, companyId, request.BankAccountCode, ct).ConfigureAwait(false);
            if (bank is null)
            {
                return NotFound($"No bank account {OrdersApiMapping.Key(request.BankAccountCode)}.");
            }

            return Clean(request.AccountCode) is not string account
                ? BadRequest("Give the account the line posts to.")
                : Value(await posting.PostToAccountAsync(companyId, bank.Value, lineId, account, Clean(request.Narrative), ct, Clean(request.TaxCode)?.ToUpperInvariant()).ConfigureAwait(false));
        }).RequireAuthorization().WithName("ApiPostStatementLineToAccount");
    }

    private static void MapTills(RouteGroupBuilder api)
    {
        api.MapGet("/api/pos/sales", async (long companyId, long userId, DateOnly? from, DateOnly? to, string? terminal, int? page, One4AllDbContext db, IDataScopeService scopes, CancellationToken ct) =>
        {
            long? terminalId = null;
            if (!string.IsNullOrWhiteSpace(terminal))
            {
                Result<long> found = await TerminalAsync(db, scopes, companyId, userId, terminal, ct).ConfigureAwait(false);
                if (found.IsFailure)
                {
                    return Value(found);
                }

                terminalId = found.Value;
            }

            var rows = await (from s in db.Sales.AsNoTracking()
                              join t in db.PosTerminals.AsNoTracking() on s.TerminalId equals t.Id
                              where s.CompanyId == companyId && (terminalId == null || s.TerminalId == terminalId) && (@from == null || s.SaleDate >= @from) && (to == null || s.SaleDate <= to)
                              orderby s.SaleDate descending, s.SaleNumber descending
                              select new
                              {
                                  s.SaleNumber, s.SaleDate, Terminal = t.Code, Cashier = db.Users.Where(u => u.Id == s.CashierUserId).Select(u => u.Username).FirstOrDefault(), s.Status,
                                  Net = s.Lines.Sum(l => l.LineNet), Tax = s.Lines.Sum(l => l.TaxAmount),
                                  Tenders = s.Tenders.Select(x => new { x.TenderType, x.Currency, x.Amount, x.BaseAmount, x.IsChange }).ToList(),
                              }).Skip(Math.Max(0, (page ?? 1) - 1) * PageSize).Take(PageSize).ToListAsync(ct).ConfigureAwait(false);
            return Results.Ok(rows.Select(r => new PosSaleRowApi(r.SaleNumber, r.SaleDate, r.Terminal, r.Cashier ?? string.Empty, r.Status.ToString(), r.Net, r.Tax, r.Net + r.Tax,
                [.. r.Tenders.Select(x => new PosSaleTenderApi(x.TenderType.ToString(), x.Currency.Value, x.Amount, x.BaseAmount, x.IsChange))])).ToList());
        }).RequireAuthorization().WithName("ApiListTillSales");

        api.MapPost("/api/pos/sales/{number}/void", async (string number, long companyId, long userId, PosVoidApi request, One4AllDbContext db, IDataScopeService scopes, IPermissionService permissions,
            IPosService pos, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(request);

            // The signed-in user authorises the void as the till's supervisor does: they need the right to void and reverse till sales.
            if (!(await permissions.ResolveAsync(userId, ct).ConfigureAwait(false)).Has("Pos.Void", OperationFlags.VoidReverse))
            {
                return Value(Result<bool>.Failure(Error.Forbidden("Api.PosVoid", "Voiding a till sale needs a supervisor's right to void (Pos.Void).")));
            }

            string key = OrdersApiMapping.Key(number);
            var sale = await db.Sales.AsNoTracking().Where(s => s.CompanyId == companyId && s.SaleNumber.ToUpper() == key).Select(s => new { s.Id, s.TerminalId }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (sale is null)
            {
                return NotFound($"No till sale {key}.");
            }

            if (!await scopes.AllowsAsync(userId, ScopeDimension.Till, sale.TerminalId, ct).ConfigureAwait(false))
            {
                return Value(Result<bool>.Failure(Error.Forbidden("Api.TillScope", $"You have no access to the till sale {key} was rung up on.")));
            }

            Result<string> reason = LedgerApiMapping.Reason(request.Reason);
            if (reason.IsFailure)
            {
                return Value(reason);
            }

            Result<string> voided = await pos.VoidSaleAsync(companyId, sale.Id, request.Date, reason.Value, Clean(request.TaxAccountCode), userId, ct).ConfigureAwait(false);
            return voided.IsFailure ? Value(voided) : Results.Ok(new { journalNumber = voided.Value });
        }).RequireAuthorization().WithName("ApiVoidTillSale");

        api.MapGet("/api/pos/tills/{code}/x-read", async (string code, long companyId, long userId, One4AllDbContext db, IDataScopeService scopes, IPosService pos, CancellationToken ct) =>
        {
            Result<long> terminal = await TerminalAsync(db, scopes, companyId, userId, code, ct).ConfigureAwait(false);
            if (terminal.IsFailure)
            {
                return Value(terminal);
            }

            var session = await db.TillSessions.AsNoTracking().Where(s => s.CompanyId == companyId && s.TerminalId == terminal.Value && s.Status == TillSessionStatus.Open).OrderByDescending(s => s.Id)
                .Select(s => new { s.Id, s.OpenedOnUtc }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (session is null)
            {
                return Value(Result<bool>.Failure(Error.Rule("Api.TillClosed", $"Till {OrdersApiMapping.Key(code)} has no open session.")));
            }

            Result<TillRead> read = await pos.XReadAsync(session.Id, ct).ConfigureAwait(false);
            return read.IsFailure ? Value(read) : Results.Ok(LedgerApiMapping.XRead(OrdersApiMapping.Key(code), session.OpenedOnUtc, read.Value));
        }).RequireAuthorization().WithName("ApiTillXRead");
    }

    private static Task<long?> BankAccountAsync(One4AllDbContext db, long companyId, string? code, CancellationToken ct)
    {
        string key = OrdersApiMapping.Key(code);
        return db.BankAccounts.AsNoTracking().Where(b => b.CompanyId == companyId && b.Code.ToUpper() == key).Select(b => (long?)b.Id).FirstOrDefaultAsync(ct);
    }

    // A till named by code, held to the user's till scope.
    private static async Task<Result<long>> TerminalAsync(One4AllDbContext db, IDataScopeService scopes, long companyId, long userId, string code, CancellationToken ct)
    {
        string key = OrdersApiMapping.Key(code);
        long? id = await db.PosTerminals.AsNoTracking().Where(t => t.CompanyId == companyId && t.Code.ToUpper() == key).Select(t => (long?)t.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (id is null)
        {
            return Error.NotFound("Api.Till", $"No till {key}.");
        }

        return await scopes.AllowsAsync(userId, ScopeDimension.Till, id.Value, ct).ConfigureAwait(false)
            ? id.Value
            : Error.Forbidden("Api.TillScope", $"You have no access to till {key}.");
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Today);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
