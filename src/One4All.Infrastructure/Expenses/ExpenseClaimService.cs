using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Expenses;
using One4All.Core.Common;
using One4All.Core.Expenses;
using One4All.Core.Payroll;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Expenses;

/// <summary>
/// Manages employee expense claims. Capture and approval touch no ledger; reimbursement posts the whole claim
/// through the central posting engine (Dr each expense account / Cr the funding account) and stamps the
/// journal number back onto the claim.
/// </summary>
public sealed class ExpenseClaimService : IExpenseClaimService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;
    private readonly Application.Abstractions.Budgeting.IBudgetGateService? _budget;
    private readonly Application.Abstractions.Security.IPermissionService? _permissions;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The central posting engine.</param>
    /// <param name="budget">The budget gate a claim passes when it is approved (brief §4.18.5).</param>
    /// <param name="permissions">Checks whether the approver may override a budget block.</param>
    public ExpenseClaimService(One4AllDbContext db, IPostingEngine posting, Application.Abstractions.Budgeting.IBudgetGateService? budget = null,
        Application.Abstractions.Security.IPermissionService? permissions = null)
    {
        _db = db;
        _posting = posting;
        _budget = budget;
        _permissions = permissions;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateAsync(long companyId, long employeeId, string reference, DateOnly claimDate, IReadOnlyList<ExpenseClaimLineRequest> lines, CancellationToken ct = default)
    {
        if (lines is null || lines.Count == 0)
        {
            return Error.Validation("Exp.NoLines", "A claim needs at least one line.");
        }

        if (lines.Any(l => l.Amount <= 0m))
        {
            return Error.Rule("Exp.NonPositiveLine", "Every claim line amount must be positive.");
        }

        if (lines.Any(l => string.IsNullOrWhiteSpace(l.ExpenseAccountCode)))
        {
            return Error.Validation("Exp.NoAccount", "Every claim line needs an expense account.");
        }

        bool employeeExists = await _db.Set<Employee>().AnyAsync(e => e.Id == employeeId && e.CompanyId == companyId, ct).ConfigureAwait(false);
        if (!employeeExists)
        {
            return Error.NotFound("Exp.EmployeeNotFound", $"Employee {employeeId} not found.");
        }

        ExpenseClaim claim = new(companyId, employeeId, reference, claimDate);
        foreach (ExpenseClaimLineRequest line in lines)
        {
            claim.AddLine(line.Description, line.ExpenseAccountCode, line.Amount);
        }

        _db.Set<ExpenseClaim>().Add(claim);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return claim.Id;
    }

    /// <inheritdoc />
    public Task<Result> ApproveAsync(long companyId, long claimId, long approvedByUserId, CancellationToken ct = default)
        => _db.AtomicAsync(() => ApproveCoreAsync(companyId, claimId, approvedByUserId, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result> ApproveCoreAsync(long companyId, long claimId, long approvedByUserId, CancellationToken ct)
    {
        Result<ExpenseClaim> loaded = await LoadAsync(companyId, claimId, ct).ConfigureAwait(false);
        if (loaded.IsFailure)
        {
            return Result.Failure(loaded.Error);
        }

        ExpenseClaim claim = loaded.Value;
        if (claim.Status != ExpenseClaimStatus.Draft)
        {
            return Result.Failure(Error.Rule("Exp.NotDraft", $"Only a draft claim can be approved; {claim.Reference} is {claim.Status}."));
        }

        // The spend is checked against what is left of each vote at approval: a blocking rule refuses it; one asking for approval needs an
        // approver who may override budget blocks.
        if (_budget is not null)
        {
            string? department = await _db.Set<Employee>().Where(e => e.Id == claim.EmployeeId).Select(e => e.Department).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            List<Application.Abstractions.Budgeting.BudgetDemand> demands = [.. claim.Lines.GroupBy(l => l.ExpenseAccountCode.Trim())
                .Select(g => new Application.Abstractions.Budgeting.BudgetDemand(g.Key, string.IsNullOrWhiteSpace(department) ? null : department.Trim().ToUpperInvariant(), g.Sum(l => l.Amount)))];
            Result<Application.Abstractions.Budgeting.BudgetGateResult> check = await _budget.CheckAsync(companyId, claim.ClaimDate, demands, ct).ConfigureAwait(false);
            if (check.IsSuccess && check.Value.Outcome == Core.Budgeting.BudgetAction.Block)
            {
                return Result.Failure(Error.Rule("Budget.Blocked", check.Value.Warning!));
            }

            if (check.IsSuccess && check.Value.Outcome == Core.Budgeting.BudgetAction.RequireApproval
                && (_permissions is null || !(await _permissions.ResolveAsync(approvedByUserId, ct).ConfigureAwait(false)).Has("Budget.OverrideBlock", Core.Security.OperationFlags.View)))
            {
                return Result.Failure(Error.Rule("Budget.ApprovalNeeded", $"{check.Value.Warning} The approver needs the right to override budget blocks."));
            }
        }

        claim.Approve(approvedByUserId);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> RejectAsync(long companyId, long claimId, CancellationToken ct = default)
    {
        Result<ExpenseClaim> loaded = await LoadAsync(companyId, claimId, ct).ConfigureAwait(false);
        if (loaded.IsFailure)
        {
            return Result.Failure(loaded.Error);
        }

        ExpenseClaim claim = loaded.Value;
        if (claim.Status != ExpenseClaimStatus.Draft)
        {
            return Result.Failure(Error.Rule("Exp.NotDraft", $"Only a draft claim can be rejected; {claim.Reference} is {claim.Status}."));
        }

        claim.Reject();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<string>> ReimburseAsync(long companyId, long claimId, DateOnly date, string fundingAccountCode, string documentNumber, CancellationToken ct = default)
        => _db.AtomicAsync(() => ReimburseCoreAsync(companyId, claimId, date, fundingAccountCode, documentNumber, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<string>> ReimburseCoreAsync(long companyId, long claimId, DateOnly date, string fundingAccountCode, string documentNumber, CancellationToken ct)
    {
        Result<ExpenseClaim> loaded = await LoadAsync(companyId, claimId, ct).ConfigureAwait(false);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        ExpenseClaim claim = loaded.Value;
        if (claim.Status != ExpenseClaimStatus.Approved)
        {
            return Error.Rule("Exp.NotApproved", $"Only an approved claim can be reimbursed; {claim.Reference} is {claim.Status}.");
        }

        // One debit per expense line, one credit to the funding account for the whole claim.
        List<JournalLineRequest> lines = claim.Lines
            .Select(l => new JournalLineRequest { AccountCode = l.ExpenseAccountCode, Side = JournalSide.Debit, TransactionAmount = l.Amount })
            .ToList();
        lines.Add(new JournalLineRequest { AccountCode = fundingAccountCode, Side = JournalSide.Credit, TransactionAmount = claim.Total });

        Company company = await _db.Set<Company>().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "EXP-REIMB",
            DocumentNumber = documentNumber,
            Narrative = $"Expense reimbursement ({claim.Reference})",
            Lines = lines,
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        claim.MarkReimbursed(posted.Value.JournalNumber);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return posted.Value.JournalNumber;
    }

    private async Task<Result<ExpenseClaim>> LoadAsync(long companyId, long claimId, CancellationToken ct)
    {
        ExpenseClaim? claim = await _db.Set<ExpenseClaim>()
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.Id == claimId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        return claim is null
            ? Error.NotFound("Exp.ClaimNotFound", $"Expense claim {claimId} not found.")
            : claim;
    }
}
