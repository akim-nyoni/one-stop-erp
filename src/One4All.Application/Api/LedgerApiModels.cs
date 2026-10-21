using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Receipting;
using ReceiptRequest = One4All.Application.Abstractions.Receipting.ReceiptRequest;

namespace One4All.Application.Api;

/// <summary>A trial balance row as the REST API reads it (base currency).</summary>
/// <param name="AccountCode">The account code.</param>
/// <param name="AccountName">The account name.</param>
/// <param name="Type">The account type.</param>
/// <param name="Debit">The debit balance.</param>
/// <param name="Credit">The credit balance.</param>
public sealed record TrialBalanceRowApi(string AccountCode, string AccountName, string Type, decimal Debit, decimal Credit);

/// <summary>The trial balance as at a date as the REST API reads it.</summary>
/// <param name="AsAt">The as-at date.</param>
/// <param name="Currency">The currency the amounts are in.</param>
/// <param name="Rows">The accounts with a balance.</param>
/// <param name="TotalDebit">The debit total.</param>
/// <param name="TotalCredit">The credit total.</param>
/// <param name="IsBalanced">Whether debits equal credits.</param>
public sealed record TrialBalanceApi(DateOnly AsAt, string Currency, IReadOnlyList<TrialBalanceRowApi> Rows, decimal TotalDebit, decimal TotalCredit, bool IsBalanced);

/// <summary>A trial balance movement row as the REST API reads it (base currency; balances are debit positive).</summary>
/// <param name="AccountCode">The account code.</param>
/// <param name="AccountName">The account name.</param>
/// <param name="Opening">The balance brought forward.</param>
/// <param name="Debit">Debits in the range.</param>
/// <param name="Credit">Credits in the range.</param>
/// <param name="Closing">The balance carried forward.</param>
public sealed record TrialBalanceMovementRowApi(string AccountCode, string AccountName, decimal Opening, decimal Debit, decimal Credit, decimal Closing);

/// <summary>The trial balance with movement between two dates as the REST API reads it.</summary>
/// <param name="From">The first day.</param>
/// <param name="To">The last day.</param>
/// <param name="Rows">The accounts with an opening, movement or closing balance.</param>
/// <param name="TotalDebit">Debits in the range.</param>
/// <param name="TotalCredit">Credits in the range.</param>
public sealed record TrialBalanceMovementApi(DateOnly From, DateOnly To, IReadOnlyList<TrialBalanceMovementRowApi> Rows, decimal TotalDebit, decimal TotalCredit);

/// <summary>A ledger account on the chart of accounts as the REST API reads it.</summary>
/// <param name="Code">The account code.</param>
/// <param name="Name">The account name.</param>
/// <param name="Type">The account type (Asset, Liability, Equity, Income, Expense).</param>
/// <param name="ReportCategory">The report category it is grouped under.</param>
/// <param name="ControlType">The sub-ledger it controls (None for an ordinary account).</param>
/// <param name="IsActive">Whether it is in use.</param>
/// <param name="AllowManualJournal">Whether manual journals may post to it.</param>
public sealed record LedgerAccountApi(string Code, string Name, string Type, string ReportCategory, string ControlType, bool IsActive, bool AllowManualJournal);

/// <summary>A customer or supplier on an aged balances report (base currency).</summary>
/// <param name="Code">The customer or supplier code.</param>
/// <param name="Name">The name.</param>
/// <param name="Current">Not yet due.</param>
/// <param name="Days1To30">1 to 30 days overdue.</param>
/// <param name="Days31To60">31 to 60 days overdue.</param>
/// <param name="Days61To90">61 to 90 days overdue.</param>
/// <param name="Days91Plus">91 days or more overdue.</param>
/// <param name="Total">The total outstanding.</param>
public sealed record AgedBalanceRowApi(string Code, string Name, decimal Current, decimal Days1To30, decimal Days31To60, decimal Days61To90, decimal Days91Plus, decimal Total);

/// <summary>Aged debtors or creditors as the REST API reads them.</summary>
/// <param name="AsAt">The as-at date.</param>
/// <param name="Rows">The customers or suppliers with a balance.</param>
/// <param name="Totals">The column totals (code and name blank).</param>
public sealed record AgedBalancesApi(DateOnly AsAt, IReadOnlyList<AgedBalanceRowApi> Rows, AgedBalanceRowApi Totals);

/// <summary>A line of a cashier receipt as the REST API takes it: what the money is for.</summary>
/// <param name="Type">Account (a payment on the payer's account), Sundry (income), Deposit or Suspense.</param>
/// <param name="Amount">The amount.</param>
/// <param name="AccountCode">The income, deposit or suspense account (not for an account payment).</param>
/// <param name="Narrative">A narrative.</param>
/// <param name="Service">The service paid for (water, rates, refuse...), for collection-by-service reporting.</param>
public sealed record CashierReceiptLineApi(string Type, decimal Amount, string? AccountCode = null, string? Narrative = null, string? Service = null);

/// <summary>A tender of a cashier receipt as the REST API takes it: how it was paid.</summary>
/// <param name="Type">Cash, Card, MobileMoney, BankTransfer or Cheque.</param>
/// <param name="Amount">The amount.</param>
/// <param name="Reference">The card, cheque or transfer reference.</param>
public sealed record CashierReceiptTenderApi(string Type, decimal Amount, string? Reference = null);

/// <summary>
/// A cashier receipt as the REST API posts it (brief §RCP): taken in the signed-in cashier's open session, against a payer named by customer code or
/// stand number (none for a sundry receipt).
/// </summary>
/// <param name="Date">The receipt date.</param>
/// <param name="CashAccountCode">The cash or bank control account the money is debited to.</param>
/// <param name="Lines">What the money is for.</param>
/// <param name="Tenders">How it was paid.</param>
/// <param name="PayerCode">The payer's customer code or stand number (blank: a sundry receipt).</param>
/// <param name="StationCode">The cashier station whose open session takes it (blank: the cashier's latest open session).</param>
/// <param name="Currency">The ISO currency paid in (blank: base).</param>
/// <param name="ExchangeRate">The rate to base for a foreign-currency payment (blank: the day's rate).</param>
/// <param name="AllocationStrategy">OldestFirst (default), NewestFirst or LargestFirst.</param>
/// <param name="AllocationReason">Why the payment is allocated other than oldest first (required then).</param>
/// <param name="Pending">Whether the tender is a claimed transfer or cheque not yet confirmed.</param>
/// <param name="PendingAccountCode">The uncleared-effects account a pending receipt posts to.</param>
/// <param name="CreditOnAccountCode">The credit-on-account account for an overpayment.</param>
public sealed record CashierReceiptApi(DateOnly Date, string CashAccountCode, IReadOnlyList<CashierReceiptLineApi> Lines, IReadOnlyList<CashierReceiptTenderApi> Tenders, string? PayerCode = null,
    string? StationCode = null, string? Currency = null, decimal? ExchangeRate = null, string? AllocationStrategy = null, string? AllocationReason = null, bool Pending = false,
    string? PendingAccountCode = null, string? CreditOnAccountCode = null);

/// <summary>A receipt reversal as the REST API posts it (by someone other than the cashier who took it).</summary>
/// <param name="Date">The reversal date.</param>
/// <param name="Reason">Why it is reversed (required).</param>
public sealed record ReceiptReversalApi(DateOnly Date, string Reason);

/// <summary>A payer found by the search.</summary>
/// <param name="Code">The account (customer) code payments go to.</param>
/// <param name="Name">The account holder.</param>
/// <param name="Matched">What matched: the account, a stand it pays rates on, or a student it pays fees for.</param>
/// <param name="Balance">What the account owes (base currency; negative: in credit).</param>
public sealed record PayerApi(string Code, string Name, string Matched, decimal Balance);

/// <summary>A money-in bank statement line posted as a customer receipt.</summary>
/// <param name="BankAccountCode">The bank account the line is on.</param>
/// <param name="CustomerCode">The paying customer.</param>
/// <param name="DocumentNumber">The receipt number (blank: the bank reference).</param>
public sealed record StatementLineReceiptApi(string BankAccountCode, string CustomerCode, string? DocumentNumber = null);

/// <summary>A money-out bank statement line posted as a supplier payment.</summary>
/// <param name="BankAccountCode">The bank account the line is on.</param>
/// <param name="SupplierCode">The supplier paid.</param>
/// <param name="DocumentNumber">The payment number (blank: the bank reference).</param>
public sealed record StatementLinePaymentApi(string BankAccountCode, string SupplierCode, string? DocumentNumber = null);

/// <summary>A bank statement line posted straight to a ledger account (charges, interest, a standing order).</summary>
/// <param name="BankAccountCode">The bank account the line is on.</param>
/// <param name="AccountCode">The contra account.</param>
/// <param name="TaxCode">The tax code when the amount includes tax.</param>
/// <param name="Narrative">A narrative (blank: the bank's description).</param>
public sealed record StatementLineToAccountApi(string BankAccountCode, string AccountCode, string? TaxCode = null, string? Narrative = null);

/// <summary>A tender on a till sale as the REST API exports it.</summary>
/// <param name="Type">The tender type.</param>
/// <param name="Currency">The currency tendered.</param>
/// <param name="Amount">The amount in that currency.</param>
/// <param name="BaseAmount">The amount in base currency.</param>
/// <param name="IsChange">Whether it is change handed back.</param>
public sealed record PosSaleTenderApi(string Type, string Currency, decimal Amount, decimal BaseAmount, bool IsChange);

/// <summary>A till sale as the REST API exports it (base currency).</summary>
/// <param name="Number">The sale number.</param>
/// <param name="Date">The sale date.</param>
/// <param name="TerminalCode">The till.</param>
/// <param name="Cashier">The cashier's user name.</param>
/// <param name="Status">Completed, Parked or Voided.</param>
/// <param name="Net">The net total.</param>
/// <param name="Tax">The tax total.</param>
/// <param name="Gross">The gross total.</param>
/// <param name="Tenders">The tenders.</param>
public sealed record PosSaleRowApi(string Number, DateOnly Date, string TerminalCode, string Cashier, string Status, decimal Net, decimal Tax, decimal Gross, IReadOnlyList<PosSaleTenderApi> Tenders);

/// <summary>A till sale void as the REST API posts it: the signed-in user is the supervisor authorising it.</summary>
/// <param name="Date">The void date.</param>
/// <param name="Reason">Why it is voided (required).</param>
/// <param name="TaxAccountCode">The output tax account (required when the sale had tax).</param>
public sealed record PosVoidApi(DateOnly Date, string Reason, string? TaxAccountCode = null);

/// <summary>An X-read of a till's open session (mid-shift, nothing reset).</summary>
/// <param name="TerminalCode">The till.</param>
/// <param name="OpenedOnUtc">When the session opened.</param>
/// <param name="SaleCount">Completed sales in the session.</param>
/// <param name="TotalGross">Their gross total (base currency).</param>
/// <param name="Tips">Tips taken (in the tender totals).</param>
/// <param name="Tenders">Tendered per type (base currency, change excluded).</param>
public sealed record TillXReadApi(string TerminalCode, DateTimeOffset OpenedOnUtc, int SaleCount, decimal TotalGross, decimal Tips, IReadOnlyDictionary<string, decimal> Tenders);

/// <summary>
/// Turns the ledger reports into the REST API's rows, and the REST API's receipting, banking and till documents into the application's requests:
/// codes resolved (from lookups the caller loads), enumerations parsed by name, bad input refused. Pure.
/// </summary>
public static class LedgerApiMapping
{
    /// <summary>The trial balance as at a date.</summary>
    /// <param name="report">The report.</param>
    /// <returns>The API view.</returns>
    public static TrialBalanceApi TrialBalance(TrialBalanceReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new TrialBalanceApi(report.AsAtDate, report.Currency, [.. report.Rows.Select(r => new TrialBalanceRowApi(r.AccountCode, r.AccountName, r.Type.ToString(), r.Debit, r.Credit))],
            report.TotalDebit, report.TotalCredit, report.IsBalanced);
    }

    /// <summary>The trial balance with movement.</summary>
    /// <param name="report">The report.</param>
    /// <returns>The API view.</returns>
    public static TrialBalanceMovementApi TrialBalanceMovement(TrialBalanceMovementReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new TrialBalanceMovementApi(report.FromDate, report.ToDate,
            [.. report.Rows.Select(r => new TrialBalanceMovementRowApi(r.AccountCode, r.AccountName, r.Opening, r.Debit, r.Credit, r.Closing))], report.Rows.Sum(r => r.Debit), report.Rows.Sum(r => r.Credit));
    }

    /// <summary>A ledger account.</summary>
    /// <param name="account">The account.</param>
    /// <returns>The API view.</returns>
    public static LedgerAccountApi Account(LedgerAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return new LedgerAccountApi(account.AccountCode, account.Name, account.Type.ToString(), account.ReportCategory, account.ControlAccountType.ToString(), account.IsActive, account.AllowManualJournal);
    }

    /// <summary>Aged debtors or creditors with their column totals.</summary>
    /// <param name="report">The report.</param>
    /// <returns>The API view.</returns>
    public static AgedBalancesApi AgedBalances(AgeingReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        List<AgedBalanceRowApi> rows = [.. report.Rows.Select(r => new AgedBalanceRowApi(r.TraderCode, r.TraderName, r.Current, r.Days1To30, r.Days31To60, r.Days61To90, r.Days91Plus, r.Total))];
        return new AgedBalancesApi(report.AsAtDate, rows, new AgedBalanceRowApi(string.Empty, string.Empty, rows.Sum(r => r.Current), rows.Sum(r => r.Days1To30), rows.Sum(r => r.Days31To60),
            rows.Sum(r => r.Days61To90), rows.Sum(r => r.Days91Plus), rows.Sum(r => r.Total)));
    }

    /// <summary>A payer found by the search.</summary>
    /// <param name="hit">The hit.</param>
    /// <returns>The API view.</returns>
    public static PayerApi Payer(PayerHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        return new PayerApi(hit.Code, hit.Name, hit.Matched, hit.Balance);
    }

    /// <summary>
    /// A cashier receipt request. An account line needs a payer; any other line needs its account. A user-directed allocation is not offered over the
    /// API (it names open items); any strategy other than oldest first needs a reason.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="sessionId">The cashier's open session.</param>
    /// <param name="api">The receipt as posted.</param>
    /// <param name="payerId">The payer (customer) named by its code or stand, or null for a sundry receipt.</param>
    /// <returns>The request, or why not.</returns>
    public static Result<ReceiptRequest> Receipt(long companyId, long sessionId, CashierReceiptApi api, long? payerId)
    {
        ArgumentNullException.ThrowIfNull(api);
        if (api.Lines is null || api.Lines.Count == 0)
        {
            return Error.Validation("Api.Lines", "A receipt needs at least one line.");
        }

        if (api.Tenders is null || api.Tenders.Count == 0)
        {
            return Error.Validation("Api.Tenders", "A receipt needs at least one tender.");
        }

        if (Clean(api.CashAccountCode) is not string cash)
        {
            return Error.Validation("Api.CashAccount", "Give the cash account the money is debited to.");
        }

        CurrencyCode? currency = null;
        if (!string.IsNullOrWhiteSpace(api.Currency))
        {
            Result<CurrencyCode> parsed = TradingApiMapping.Currency(api.Currency);
            if (parsed.IsFailure)
            {
                return parsed.Error;
            }

            currency = parsed.Value;
        }

        ReceiptAllocationStrategy strategy = ReceiptAllocationStrategy.OldestFirst;
        if (!string.IsNullOrWhiteSpace(api.AllocationStrategy)
            && (!Enum.TryParse(api.AllocationStrategy.Trim(), ignoreCase: true, out strategy) || !Enum.IsDefined(strategy) || strategy == ReceiptAllocationStrategy.UserDirected))
        {
            return Error.Validation("Api.AllocationStrategy", $"'{api.AllocationStrategy}' is not an allocation strategy (OldestFirst, NewestFirst, LargestFirst).");
        }

        if (strategy != ReceiptAllocationStrategy.OldestFirst && Clean(api.AllocationReason) is null)
        {
            return Error.Validation("Api.AllocationReason", "Say why the payment is not allocated oldest first.");
        }

        List<ReceiptLineRequest> lines = [];
        foreach (CashierReceiptLineApi line in api.Lines)
        {
            Result<ReceiptLineRequest> mapped = ReceiptLine(line, payerId);
            if (mapped.IsFailure)
            {
                return mapped.Error;
            }

            lines.Add(mapped.Value);
        }

        List<ReceiptTenderRequest> tenders = [];
        foreach (CashierReceiptTenderApi tender in api.Tenders)
        {
            if (!TryName(tender.Type, out TenderType type))
            {
                return Error.Validation("Api.Tender", $"'{tender.Type}' is not a tender ({string.Join(", ", Enum.GetNames<TenderType>())}).");
            }

            if (tender.Amount <= 0m)
            {
                return Error.Validation("Api.Amount", "A tender must be more than zero.");
            }

            tenders.Add(new ReceiptTenderRequest(type, tender.Amount, Clean(tender.Reference)));
        }

        return new ReceiptRequest
        {
            CompanyId = companyId, SessionId = sessionId, PayerKind = payerId is null ? PayerKind.Anonymous : PayerKind.ArCustomer, PayerAccountId = payerId, Date = api.Date, Currency = currency,
            ExchangeRate = api.ExchangeRate, CashAccountCode = cash, Pending = api.Pending, PendingAccountCode = Clean(api.PendingAccountCode), CreditOnAccountCode = Clean(api.CreditOnAccountCode),
            AllocationStrategy = strategy, AllocationReason = Clean(api.AllocationReason), Lines = lines, Tenders = tenders,
        };
    }

    /// <summary>The reason for a reversal or void, required.</summary>
    /// <param name="reason">The reason given.</param>
    /// <returns>The trimmed reason, or a validation error.</returns>
    public static Result<string> Reason(string? reason)
        => Clean(reason) is string given ? given : Error.Validation("Api.Reason", "Give the reason.");

    /// <summary>An X-read of a till's open session.</summary>
    /// <param name="terminalCode">The till.</param>
    /// <param name="openedOnUtc">When the session opened.</param>
    /// <param name="read">The read.</param>
    /// <returns>The API view.</returns>
    public static TillXReadApi XRead(string terminalCode, DateTimeOffset openedOnUtc, TillRead read)
    {
        ArgumentNullException.ThrowIfNull(read);
        return new TillXReadApi(terminalCode, openedOnUtc, read.SaleCount, read.TotalGross, read.Tips,
            read.TenderTotals.OrderBy(t => t.Key).ToDictionary(t => t.Key.ToString(), t => t.Value, StringComparer.Ordinal));
    }

    private static Result<ReceiptLineRequest> ReceiptLine(CashierReceiptLineApi line, long? payerId)
    {
        ArgumentNullException.ThrowIfNull(line);
        string kind = (line.Type ?? string.Empty).Trim();
        ReceiptLineType? type = kind.ToUpperInvariant() switch
        {
            "ACCOUNT" or "DEBTORPAYMENT" => ReceiptLineType.DebtorPayment,
            "SUNDRY" => ReceiptLineType.Sundry,
            "DEPOSIT" => ReceiptLineType.Deposit,
            "SUSPENSE" => ReceiptLineType.Suspense,
            _ => null,
        };
        if (type is null)
        {
            return Error.Validation("Api.LineType", $"'{line.Type}' is not a receipt line (Account, Sundry, Deposit, Suspense).");
        }

        if (line.Amount <= 0m)
        {
            return Error.Validation("Api.Amount", "A receipt line must be more than zero.");
        }

        string? account = Clean(line.AccountCode);
        if (type == ReceiptLineType.DebtorPayment)
        {
            return payerId is null
                ? Error.Validation("Api.Payer", "A payment on an account needs the payer (customer code or stand number).")
                : new ReceiptLineRequest(ReceiptLineType.DebtorPayment, line.Amount, null, Clean(line.Narrative), Clean(line.Service));
        }

        return account is null
            ? Error.Validation("Api.Account", $"A {type.Value.ToString().ToLowerInvariant()} line needs its account.")
            : new ReceiptLineRequest(type.Value, line.Amount, account, Clean(line.Narrative), Clean(line.Service));
    }

    private static bool TryName<TEnum>(string? text, out TEnum value)
        where TEnum : struct, Enum
        => Enum.TryParse(text?.Trim(), ignoreCase: true, out value) && Enum.IsDefined(value);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
