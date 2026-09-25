using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Common;
using One4All.Core.Payroll;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Payroll;

/// <summary>Manages employee loans, posting the advance and repayments through the central posting engine.</summary>
public sealed class EmployeeLoanService : IEmployeeLoanService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The central posting engine.</param>
    public EmployeeLoanService(One4AllDbContext db, IPostingEngine posting)
    {
        _db = db;
        _posting = posting;
    }

    /// <inheritdoc />
    public async Task<Result<long>> GrantAsync(long companyId, long employeeId, string reference, decimal principal, decimal installment, DateOnly date, string loanReceivableAccountCode, string fundingAccountCode, string documentNumber, CancellationToken ct = default)
    {
        if (principal <= 0m)
        {
            return Error.Rule("Pay.LoanNonPositive", "The loan principal must be positive.");
        }

        bool employeeExists = await _db.Set<Employee>().AnyAsync(e => e.Id == employeeId && e.CompanyId == companyId, ct).ConfigureAwait(false);
        if (!employeeExists)
        {
            return Error.NotFound("Pay.EmployeeNotFound", $"Employee {employeeId} not found.");
        }

        Company company = await _db.Set<Company>().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "PAY-LOAN",
            DocumentNumber = documentNumber,
            Narrative = $"Loan advance to employee #{employeeId} ({reference})",
            Lines =
            [
                new JournalLineRequest { AccountCode = loanReceivableAccountCode, Side = JournalSide.Debit, TransactionAmount = principal },
                new JournalLineRequest { AccountCode = fundingAccountCode, Side = JournalSide.Credit, TransactionAmount = principal },
            ],
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        EmployeeLoan loan = new(companyId, employeeId, reference, principal, installment, date, loanReceivableAccountCode);
        _db.Set<EmployeeLoan>().Add(loan);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return loan.Id;
    }

    /// <inheritdoc />
    public async Task<Result<decimal>> RecordRepaymentAsync(long companyId, long loanId, decimal amount, DateOnly date, string fundingAccountCode, string documentNumber, CancellationToken ct = default)
    {
        if (amount <= 0m)
        {
            return Error.Rule("Pay.RepaymentNonPositive", "The repayment must be positive.");
        }

        EmployeeLoan? loan = await _db.Set<EmployeeLoan>().FirstOrDefaultAsync(l => l.Id == loanId && l.CompanyId == companyId, ct).ConfigureAwait(false);
        if (loan is null)
        {
            return Error.NotFound("Pay.LoanNotFound", $"Loan {loanId} not found.");
        }

        if (loan.Status == EmployeeLoanStatus.Settled)
        {
            return Error.Rule("Pay.LoanSettled", $"Loan {loan.Reference} is already settled.");
        }

        if (amount > loan.Outstanding)
        {
            return Error.Rule("Pay.RepaymentTooLarge", $"The repayment {amount:0.00} exceeds the outstanding balance {loan.Outstanding:0.00}.");
        }

        Company company = await _db.Set<Company>().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "PAY-LOAN-REP",
            DocumentNumber = documentNumber,
            Narrative = $"Loan repayment ({loan.Reference})",
            Lines =
            [
                new JournalLineRequest { AccountCode = fundingAccountCode, Side = JournalSide.Debit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = loan.LoanReceivableAccountCode, Side = JournalSide.Credit, TransactionAmount = amount },
            ],
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        loan.Repay(amount);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return loan.Outstanding;
    }
}
