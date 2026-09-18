using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.JobCosting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.JobCosting;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.JobCosting;

/// <summary>
/// Construction / Job Costing (brief §4.12). Contracts accumulate captured costs for profitability;
/// interim payment certificates raise the net payable on the client's AR account through the central
/// ledger and recognise retention as a separate receivable — the gross is credited to contract revenue.
/// </summary>
public sealed class JobCostingService : IJobCostingService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;
    private readonly ISalesLedgerService _salesLedger;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The central posting engine.</param>
    /// <param name="salesLedger">The AR ledger.</param>
    public JobCostingService(One4AllDbContext db, IPostingEngine posting, ISalesLedgerService salesLedger)
    {
        _db = db;
        _posting = posting;
        _salesLedger = salesLedger;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateContractAsync(long companyId, string contractNumber, string name, long customerId, decimal contractValue, decimal budgetCost, decimal retentionPercent, string revenueAccountCode, CancellationToken ct = default)
    {
        JobContract contract = new(companyId, contractNumber, name, customerId, contractValue, budgetCost, retentionPercent, revenueAccountCode);
        _db.Set<JobContract>().Add(contract);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return contract.Id;
    }

    /// <inheritdoc />
    public async Task<Result> CaptureCostAsync(long companyId, long contractId, string costHead, JobCostSource source, decimal amount, DateOnly date, string? reference, CancellationToken ct = default)
    {
        JobContract? contract = await _db.Set<JobContract>().Include(c => c.Costs)
            .FirstOrDefaultAsync(c => c.Id == contractId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (contract is null)
        {
            return Result.Failure(Error.NotFound("Job.ContractNotFound", $"Contract {contractId} not found."));
        }

        contract.CaptureCost(costHead, source, amount, date, reference);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<CertificateResult>> CertifyProgressAsync(long companyId, long contractId, string certificateNumber, DateOnly date, decimal workDoneToDate, decimal materialsOnSite, string retentionReceivableAccountCode, CancellationToken ct = default)
    {
        JobContract? contract = await _db.Set<JobContract>()
            .FirstOrDefaultAsync(c => c.Id == contractId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (contract is null)
        {
            return Error.NotFound("Job.ContractNotFound", $"Contract {contractId} not found.");
        }

        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Job.CompanyNotFound", $"Company {companyId} not found.");
        }

        ProgressCertificate certificate = new(companyId, contractId, certificateNumber, date, workDoneToDate, materialsOnSite, contract.CertifiedToDate, contract.RetentionPercent);
        if (certificate.GrossThisCertificate <= 0m)
        {
            return Error.Rule("Job.NothingToCertify", $"Certificate {certificateNumber} values no new work (gross {certificate.GrossThisCertificate}).");
        }

        _db.Set<ProgressCertificate>().Add(certificate);

        // Net payable raised on the client's AR account (Dr AR / Cr contract revenue for the net).
        Result<TradingPostingResult> ar = await _salesLedger.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId,
            CustomerId = contract.CustomerId,
            DocumentNumber = certificateNumber,
            DocumentDate = date,
            Currency = company.BaseCurrency,
            NetAmount = certificate.NetPayable,
            TaxAmount = 0m,
            SalesAccountCode = contract.RevenueAccountCode,
            Narrative = $"IPC {certificateNumber} — {contract.ContractNumber}",
        }, ct).ConfigureAwait(false);

        if (ar.IsFailure)
        {
            return ar.Error;
        }

        // Retention recognised as a receivable, also credited to contract revenue (so total revenue = gross).
        string? retentionJournal = null;
        if (certificate.RetentionThisCertificate > 0m)
        {
            Result<PostingReceipt> retention = await _posting.PostAsync(new JournalRequest
            {
                CompanyId = companyId,
                SourceModule = "GL",
                PostingDate = date,
                TransactionCurrency = company.BaseCurrency,
                DocumentType = "RETENTION",
                DocumentNumber = certificateNumber,
                Narrative = $"Retention held — IPC {certificateNumber}",
                Lines =
                [
                    new JournalLineRequest { AccountCode = retentionReceivableAccountCode, Side = JournalSide.Debit, TransactionAmount = certificate.RetentionThisCertificate },
                    new JournalLineRequest { AccountCode = contract.RevenueAccountCode, Side = JournalSide.Credit, TransactionAmount = certificate.RetentionThisCertificate },
                ],
            }, ct).ConfigureAwait(false);

            if (retention.IsFailure)
            {
                return retention.Error;
            }

            retentionJournal = retention.Value.JournalNumber;
        }

        contract.ApplyCertificate(certificate.GrossThisCertificate, certificate.RetentionThisCertificate);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new CertificateResult(certificate.Id, certificate.GrossThisCertificate, certificate.RetentionThisCertificate, certificate.NetPayable, ar.Value.OpenItemId, ar.Value.JournalNumber, retentionJournal);
    }

    /// <inheritdoc />
    public async Task<Result<JobProfitability>> GetProfitabilityAsync(long companyId, long contractId, CancellationToken ct = default)
    {
        JobContract? contract = await _db.Set<JobContract>().Include(c => c.Costs)
            .FirstOrDefaultAsync(c => c.Id == contractId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (contract is null)
        {
            return Error.NotFound("Job.ContractNotFound", $"Contract {contractId} not found.");
        }

        return new JobProfitability(contract.ContractValue, contract.BudgetCost, contract.ActualCost, contract.CertifiedToDate, contract.PercentComplete);
    }
}
