using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>
/// Petty cash on the imprest system: disburses expenses from the float and reimburses the float back to its
/// set amount by drawing the spent total from the bank, all through the central posting engine.
/// </summary>
public sealed class PettyCashService : IPettyCashService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _postingEngine;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="postingEngine">The central posting engine.</param>
    public PettyCashService(One4AllDbContext db, IPostingEngine postingEngine)
    {
        _db = db;
        _postingEngine = postingEngine;
    }

    /// <inheritdoc />
    public Task<Result<string>> DisburseAsync(long companyId, decimal amount, DateOnly date, string expenseAccountCode, string pettyCashAccountCode, string documentNumber, CancellationToken ct = default)
        => _db.AtomicAsync(() => DisburseCoreAsync(companyId, amount, date, expenseAccountCode, pettyCashAccountCode, documentNumber, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<string>> DisburseCoreAsync(long companyId, decimal amount, DateOnly date, string expenseAccountCode, string pettyCashAccountCode, string documentNumber, CancellationToken ct)
    {
        if (amount <= 0m)
        {
            return Error.Validation("PettyCash.Amount", "A disbursement amount must be positive.");
        }

        Result<PostingReceipt> posting = await PostAsync(companyId, date, "CB", documentNumber, $"Petty cash disbursement {documentNumber}",
            (expenseAccountCode, JournalSide.Debit, amount),
            (pettyCashAccountCode, JournalSide.Credit, amount)).ConfigureAwait(false);

        return posting.IsSuccess ? posting.Value.JournalNumber : posting.Error;
    }

    /// <inheritdoc />
    public Task<Result<ImprestResult>> ReimburseToFloatAsync(long companyId, DateOnly date, string pettyCashAccountCode, string bankAccountCode, decimal floatAmount, string documentNumber, CancellationToken ct = default)
        => _db.AtomicAsync(() => ReimburseToFloatCoreAsync(companyId, date, pettyCashAccountCode, bankAccountCode, floatAmount, documentNumber, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<ImprestResult>> ReimburseToFloatCoreAsync(long companyId, DateOnly date, string pettyCashAccountCode, string bankAccountCode, decimal floatAmount, string documentNumber, CancellationToken ct)
    {
        if (floatAmount <= 0m)
        {
            return Error.Validation("PettyCash.Float", "The float amount must be positive.");
        }

        decimal balance = await BalanceAsync(companyId, pettyCashAccountCode, date, ct).ConfigureAwait(false);
        decimal shortfall = decimal.Round(floatAmount - balance, 2, MidpointRounding.AwayFromZero);
        if (shortfall <= 0m)
        {
            return new ImprestResult(0m, balance, null); // float already full
        }

        Result<PostingReceipt> posting = await PostAsync(companyId, date, "CB", documentNumber, $"Petty cash reimbursement {documentNumber}",
            (pettyCashAccountCode, JournalSide.Debit, shortfall),
            (bankAccountCode, JournalSide.Credit, shortfall)).ConfigureAwait(false);

        if (posting.IsFailure)
        {
            return posting.Error;
        }

        return new ImprestResult(shortfall, balance + shortfall, posting.Value.JournalNumber);
    }

    /// <inheritdoc />
    public Task<Result<PettyCashFund>> SaveFundAsync(long companyId, PettyCashFundInput input, CancellationToken ct = default)
        => _db.AtomicAsync(() => SaveFundCoreAsync(companyId, input, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<PettyCashFund>> SaveFundCoreAsync(long companyId, PettyCashFundInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Code) || string.IsNullOrWhiteSpace(input.Name))
        {
            return Error.Validation("PettyCash.Fund", "A fund needs a code and a name.");
        }

        if (input.FloatAmount <= 0m)
        {
            return Error.Validation("PettyCash.Float", "The float amount must be positive.");
        }

        foreach (string? code in new[] { input.AccountCode, input.BankAccountCode, input.OverShortAccountCode })
        {
            if (code is not null && !await _db.Accounts.AnyAsync(a => a.CompanyId == companyId && a.AccountCode == code, ct).ConfigureAwait(false))
            {
                return Error.NotFound("PettyCash.Account", $"Ledger account '{code}' does not exist.");
            }
        }

        if (string.IsNullOrWhiteSpace(input.AccountCode) || string.IsNullOrWhiteSpace(input.BankAccountCode))
        {
            return Error.Validation("PettyCash.Accounts", "A fund needs its petty cash account and the bank account it is reimbursed from.");
        }

        long? custodian = null;
        if (!string.IsNullOrWhiteSpace(input.CustodianUsername))
        {
            string normalized = input.CustodianUsername.Trim().ToUpperInvariant();
            custodian = await _db.Users.Where(u => u.NormalizedUsername == normalized).Select(u => (long?)u.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (custodian is null)
            {
                return Error.NotFound("PettyCash.Custodian", $"User '{input.CustodianUsername}' does not exist.");
            }
        }

        long? branch = null;
        if (!string.IsNullOrWhiteSpace(input.BranchCode))
        {
            string branchCode = input.BranchCode.Trim().ToUpperInvariant();
            branch = await _db.Branches.Where(b => b.CompanyId == companyId && b.Code.ToUpper() == branchCode).Select(b => (long?)b.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (branch is null)
            {
                return Error.NotFound("PettyCash.Branch", $"Branch '{input.BranchCode}' does not exist.");
            }
        }

        string fundCode = input.Code.Trim().ToUpperInvariant();
        PettyCashFund? fund = await _db.Set<PettyCashFund>().FirstOrDefaultAsync(f => f.CompanyId == companyId && f.Code == fundCode, ct).ConfigureAwait(false);
        if (fund is null)
        {
            fund = new PettyCashFund(companyId, fundCode, input.Name.Trim(), input.AccountCode, input.BankAccountCode, input.FloatAmount);
            _db.Set<PettyCashFund>().Add(fund);
        }
        else
        {
            fund.Name = input.Name.Trim();
            fund.AccountCode = input.AccountCode;
            fund.BankAccountCode = input.BankAccountCode;
            fund.FloatAmount = input.FloatAmount;
        }

        fund.OverShortAccountCode = input.OverShortAccountCode;
        fund.CustodianUserId = custodian;
        fund.BranchId = branch;
        fund.IsActive = input.IsActive;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return fund;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PettyCashFundRow>> ListFundsAsync(long companyId, CancellationToken ct = default)
    {
        List<PettyCashFund> funds = await _db.Set<PettyCashFund>().AsNoTracking().Where(f => f.CompanyId == companyId).OrderBy(f => f.Code).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> users = await _db.Users.AsNoTracking().Where(u => funds.Select(f => f.CustodianUserId).Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct).ConfigureAwait(false);
        Dictionary<long, DateOnly> lastCounts = await _db.Set<PettyCashCount>().AsNoTracking().Where(c => c.CompanyId == companyId).GroupBy(c => c.FundId)
            .Select(g => new { FundId = g.Key, Date = g.Max(c => c.CountDate) }).ToDictionaryAsync(x => x.FundId, x => x.Date, ct).ConfigureAwait(false);
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        List<PettyCashFundRow> rows = [];
        foreach (PettyCashFund fund in funds)
        {
            decimal balance = await BalanceAsync(companyId, fund.AccountCode, today, ct).ConfigureAwait(false);
            rows.Add(new PettyCashFundRow(fund.Id, fund.Code, fund.Name, fund.AccountCode, fund.FloatAmount, balance,
                fund.CustodianUserId is long u && users.TryGetValue(u, out string? name) ? name : null, fund.IsActive, lastCounts.TryGetValue(fund.Id, out DateOnly d) ? d : null));
        }

        return rows;
    }

    /// <inheritdoc />
    public Task<Result<PettyCashDisbursementResult>> DisburseFromFundAsync(long companyId, PettyCashDisbursement request, CancellationToken ct = default)
        => _db.AtomicAsync(() => DisburseFromFundCoreAsync(companyId, request, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<PettyCashDisbursementResult>> DisburseFromFundCoreAsync(long companyId, PettyCashDisbursement request, CancellationToken ct)
    {
        if (request.Amount <= 0m)
        {
            return Error.Validation("PettyCash.Amount", "A disbursement amount must be positive.");
        }

        if (string.IsNullOrWhiteSpace(request.ExpenseAccountCode))
        {
            return Error.Validation("PettyCash.Expense", "A disbursement needs the expense account.");
        }

        Result<PettyCashFund> fundLookup = await FundAsync(companyId, request.FundCode, ct).ConfigureAwait(false);
        if (fundLookup.IsFailure)
        {
            return fundLookup.Error;
        }

        PettyCashFund fund = fundLookup.Value;
        decimal balance = await BalanceAsync(companyId, fund.AccountCode, request.Date, ct).ConfigureAwait(false);
        if (request.Amount > balance)
        {
            return Error.Rule("PettyCash.Insufficient", $"Fund {fund.Code} holds {balance:N2}; it cannot pay {request.Amount:N2}. Reimburse the float first.");
        }

        // Input tax on the receipt: the recoverable share goes to the input tax account, the rest stays in the expense.
        decimal inputTax = 0m;
        string? inputTaxAccount = null;
        if (!string.IsNullOrWhiteSpace(request.TaxCode))
        {
            string taxCode = request.TaxCode.Trim().ToUpperInvariant();
            TaxCode? tax = await _db.TaxCodes.Include(x => x.Rates).FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Code == taxCode && x.IsActive, ct).ConfigureAwait(false);
            if (tax is null)
            {
                return Error.NotFound("PettyCash.TaxCode", $"Tax code '{request.TaxCode}' does not exist.");
            }

            if (tax.Type is TaxType.Standard or TaxType.Deemed)
            {
                decimal rate = tax.RateOn(request.Date);
                decimal taxOnReceipt = decimal.Round(request.Amount - request.Amount / (1m + rate / 100m), 2, MidpointRounding.AwayFromZero);
                inputTax = decimal.Round(taxOnReceipt * tax.RecoverablePercent / 100m, 2, MidpointRounding.AwayFromZero);
                if (inputTax > 0m)
                {
                    inputTaxAccount = tax.InputAccountCode ?? await _db.Accounts.Where(a => a.CompanyId == companyId && a.ControlAccountType == ControlAccountType.VatInput)
                        .OrderBy(a => a.AccountCode).Select(a => a.AccountCode).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                    if (inputTaxAccount is null)
                    {
                        return Error.Rule("PettyCash.InputTaxAccount", $"Tax code {tax.Code} has no input tax account and the company has no VAT input control account.");
                    }
                }
            }
        }

        decimal net = request.Amount - inputTax;
        string narrative = request.Narrative ?? $"Petty cash {request.DocumentNumber}" + (request.Payee is null ? string.Empty : $" to {request.Payee}");
        List<JournalLineRequest> lines =
        [
            new() { AccountCode = request.ExpenseAccountCode, Side = JournalSide.Debit, TransactionAmount = net, CostCentre = request.CostCentre, Department = request.Department, TaxCode = request.TaxCode, Narrative = narrative },
        ];
        if (inputTax > 0m)
        {
            lines.Add(new JournalLineRequest { AccountCode = inputTaxAccount!, Side = JournalSide.Debit, TransactionAmount = inputTax, TaxCode = request.TaxCode, Narrative = "Input tax" });
        }

        lines.Add(new JournalLineRequest { AccountCode = fund.AccountCode, Side = JournalSide.Credit, TransactionAmount = request.Amount, Narrative = narrative });
        Result<PostingReceipt> posting = await PostLinesAsync(companyId, request.Date, request.DocumentNumber, $"Petty cash disbursement {request.DocumentNumber} ({fund.Code})", lines).ConfigureAwait(false);
        return posting.IsSuccess ? new PettyCashDisbursementResult(posting.Value.JournalNumber, net, inputTax, balance - request.Amount) : posting.Error;
    }

    /// <inheritdoc />
    public Task<Result<ImprestResult>> ReimburseFundAsync(long companyId, string fundCode, DateOnly date, string documentNumber, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            Result<PettyCashFund> fund = await FundAsync(companyId, fundCode, ct).ConfigureAwait(false);
            return fund.IsFailure ? fund.Error
                : await ReimburseToFloatCoreAsync(companyId, date, fund.Value.AccountCode, fund.Value.BankAccountCode, fund.Value.FloatAmount, documentNumber, ct).ConfigureAwait(false);
        }, ct);

    /// <inheritdoc />
    public Task<Result<PettyCashCountResult>> CountAsync(long companyId, PettyCashCountInput input, CancellationToken ct = default)
        => _db.AtomicAsync(() => CountCoreAsync(companyId, input, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<PettyCashCountResult>> CountCoreAsync(long companyId, PettyCashCountInput input, CancellationToken ct)
    {
        if (input.CountedAmount < 0m)
        {
            return Error.Validation("PettyCash.Counted", "The counted amount cannot be negative.");
        }

        Result<PettyCashFund> fund = await FundAsync(companyId, input.FundCode, ct).ConfigureAwait(false);
        if (fund.IsFailure)
        {
            return fund.Error;
        }

        decimal book = await BalanceAsync(companyId, fund.Value.AccountCode, input.Date, ct).ConfigureAwait(false);
        PettyCashCount count = new(companyId, fund.Value.Id, input.Date, input.CountedAmount, book, input.CountedByUserId, input.Denominations, input.Note);
        _db.Set<PettyCashCount>().Add(count);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new PettyCashCountResult(count.Id, book, input.CountedAmount, count.Variance);
    }

    /// <inheritdoc />
    public Task<Result<PettyCashReconciliation>> ReconcileAsync(long companyId, long countId, DateOnly date, string documentNumber, long userId, CancellationToken ct = default)
        => _db.AtomicAsync(() => ReconcileCoreAsync(companyId, countId, date, documentNumber, userId, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<PettyCashReconciliation>> ReconcileCoreAsync(long companyId, long countId, DateOnly date, string documentNumber, long userId, CancellationToken ct)
    {
        PettyCashCount? count = await _db.Set<PettyCashCount>().FirstOrDefaultAsync(c => c.CompanyId == companyId && c.Id == countId, ct).ConfigureAwait(false);
        if (count is null)
        {
            return Error.NotFound("PettyCash.Count", $"Count {countId} does not exist.");
        }

        if (count.Status == PettyCashCountStatus.Reconciled)
        {
            return Error.Conflict("PettyCash.Reconciled", $"Count {countId} is already reconciled.");
        }

        if (date < count.CountDate)
        {
            return Error.Validation("PettyCash.Date", "A count is reconciled on or after the day it was counted.");
        }

        PettyCashFund fund = await _db.Set<PettyCashFund>().FirstAsync(f => f.Id == count.FundId, ct).ConfigureAwait(false);
        string? varianceJournal = null;
        if (count.Variance != 0m)
        {
            if (string.IsNullOrWhiteSpace(fund.OverShortAccountCode))
            {
                return Error.Rule("PettyCash.OverShortAccount", $"Fund {fund.Code} has a variance of {count.Variance:N2} but no over/short account to post it to.");
            }

            decimal amount = Math.Abs(count.Variance);
            // Shortage: the book is higher than the cash - write the cash down. Overage: take the extra cash in.
            Result<PostingReceipt> variance = count.Variance < 0m
                ? await PostAsync(companyId, date, "CB", documentNumber, $"Petty cash shortage {fund.Code} count {count.CountDate:yyyy-MM-dd}",
                    (fund.OverShortAccountCode, JournalSide.Debit, amount), (fund.AccountCode, JournalSide.Credit, amount)).ConfigureAwait(false)
                : await PostAsync(companyId, date, "CB", documentNumber, $"Petty cash overage {fund.Code} count {count.CountDate:yyyy-MM-dd}",
                    (fund.AccountCode, JournalSide.Debit, amount), (fund.OverShortAccountCode, JournalSide.Credit, amount)).ConfigureAwait(false);
            if (variance.IsFailure)
            {
                return variance.Error;
            }

            varianceJournal = variance.Value.JournalNumber;
        }

        Result<ImprestResult> reimbursed = await ReimburseToFloatCoreAsync(companyId, date, fund.AccountCode, fund.BankAccountCode, fund.FloatAmount, documentNumber, ct).ConfigureAwait(false);
        if (reimbursed.IsFailure)
        {
            return reimbursed.Error;
        }

        count.Reconcile(userId, varianceJournal, reimbursed.Value.JournalNumber);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new PettyCashReconciliation(count.Variance, varianceJournal, reimbursed.Value.ReimbursedAmount, reimbursed.Value.JournalNumber, reimbursed.Value.FloatBalanceAfter);
    }

    private async Task<Result<PettyCashFund>> FundAsync(long companyId, string? fundCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fundCode))
        {
            return Error.Validation("PettyCash.Fund", "A fund code is required.");
        }

        string code = fundCode.Trim().ToUpperInvariant();
        PettyCashFund? fund = await _db.Set<PettyCashFund>().FirstOrDefaultAsync(f => f.CompanyId == companyId && f.Code == code, ct).ConfigureAwait(false);
        if (fund is null)
        {
            return Error.NotFound("PettyCash.Fund", $"Petty cash fund '{fundCode}' does not exist.");
        }

        return fund.IsActive ? fund : Error.Rule("PettyCash.Inactive", $"Petty cash fund {fund.Code} is closed.");
    }

    private async Task<decimal> BalanceAsync(long companyId, string accountCode, DateOnly asAt, CancellationToken ct)
    {
        var totals = await _db.Set<JournalLine>()
            .Where(l => l.AccountCode == accountCode && _db.Journals.Any(j =>
                j.Id == l.JournalEntryId && j.CompanyId == companyId && j.Status == JournalStatus.Posted && j.PostingDate <= asAt))
            .GroupBy(l => 1)
            .Select(g => new { Debit = g.Sum(x => x.BaseDebit), Credit = g.Sum(x => x.BaseCredit) })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        return totals is null ? 0m : totals.Debit - totals.Credit;
    }

    private Task<Result<PostingReceipt>> PostAsync(long companyId, DateOnly date, string module, string documentNumber, string narrative, params (string Account, JournalSide Side, decimal Amount)[] lines)
        => PostLinesAsync(companyId, date, documentNumber, narrative, lines.Select(l => new JournalLineRequest { AccountCode = l.Account, Side = l.Side, TransactionAmount = l.Amount }).ToList());

    private async Task<Result<PostingReceipt>> PostLinesAsync(long companyId, DateOnly date, string documentNumber, string narrative, List<JournalLineRequest> lines)
    {
        const string module = "CB";
        Company company = await _db.Companies.FirstAsync(c => c.Id == companyId).ConfigureAwait(false);
        return await _postingEngine.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = module,
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            EnforceManualJournalRules = false,
            DocumentType = "PettyCash",
            DocumentNumber = documentNumber,
            Narrative = narrative,
            Lines = lines,
        }).ConfigureAwait(false);
    }
}
