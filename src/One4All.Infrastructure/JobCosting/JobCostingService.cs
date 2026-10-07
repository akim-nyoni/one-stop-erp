using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.JobCosting;
using One4All.Application.Abstractions.Trading;
using One4All.Application.JobCosting;
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
    public Task<Result> CaptureCostAsync(long companyId, long contractId, string costHead, JobCostSource source, decimal amount, DateOnly date, string? reference, CancellationToken ct = default)
        => _db.AtomicAsync(() => CaptureCostCoreAsync(companyId, contractId, costHead, source, amount, date, reference, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result> CaptureCostCoreAsync(long companyId, long contractId, string costHead, JobCostSource source, decimal amount, DateOnly date, string? reference, CancellationToken ct)
    {
        JobContract? contract = await _db.Set<JobContract>().Include(c => c.Costs)
            .FirstOrDefaultAsync(c => c.Id == contractId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (contract is null)
        {
            return Result.Failure(Error.NotFound("Job.ContractNotFound", $"Contract {contractId} not found."));
        }

        await JobCosts.CaptureAsync(_db, contract, costHead, source, amount, date, reference, ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<CertificateResult>> CertifyProgressAsync(long companyId, long contractId, string certificateNumber, DateOnly date, decimal workDoneToDate, decimal materialsOnSite, string retentionReceivableAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => CertifyProgressCoreAsync(companyId, contractId, certificateNumber, date, workDoneToDate, materialsOnSite, retentionReceivableAccountCode, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<CertificateResult>> CertifyProgressCoreAsync(long companyId, long contractId, string certificateNumber, DateOnly date, decimal workDoneToDate, decimal materialsOnSite, string retentionReceivableAccountCode, CancellationToken ct)
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

        // Committed: ordered for the job on live purchase orders and not yet received.
        decimal committed = (await (from line in _db.Set<Core.Procurement.PurchaseOrderLine>()
                                    join order in _db.Set<Core.Procurement.PurchaseOrder>() on line.PurchaseOrderId equals order.Id
                                    where line.JobContractId == contractId && order.CompanyId == companyId
                                        && order.Status != Core.Procurement.PurchaseOrderStatus.Cancelled && order.Status != Core.Procurement.PurchaseOrderStatus.Closed
                                        && line.OrderedQuantity > line.ReceivedQuantity
                                    select new { line.OrderedQuantity, line.ReceivedQuantity, line.UnitPrice }).ToListAsync(ct).ConfigureAwait(false))
            .Sum(l => decimal.Round((l.OrderedQuantity - l.ReceivedQuantity) * l.UnitPrice, 2, MidpointRounding.AwayFromZero));
        return new JobProfitability(contract.ContractValue, contract.BudgetCost, contract.ActualCost, contract.CertifiedToDate, contract.PercentComplete)
        {
            CommittedCost = committed,
            Overheads = contract.Costs.Where(c => c.Source == JobCostSource.Overhead).Sum(c => c.Amount),
        };
    }

    /// <inheritdoc />
    public async Task<Result> SetOverheadRateAsync(long companyId, JobCostSource appliesTo, decimal percent, string costHead, CancellationToken ct = default)
    {
        if (appliesTo == JobCostSource.Overhead || percent < 0m || percent > 500m || (percent > 0m && string.IsNullOrWhiteSpace(costHead)))
        {
            return Result.Failure(Error.Validation("Job.OverheadRate", "An overhead rate is 0-500% on a direct cost (not on overheads), with the cost head it goes to."));
        }

        JobOverheadRate? rate = await _db.Set<JobOverheadRate>().FirstOrDefaultAsync(r => r.CompanyId == companyId && r.AppliesTo == appliesTo, ct).ConfigureAwait(false);
        if (percent == 0m)
        {
            if (rate is not null)
            {
                _db.Set<JobOverheadRate>().Remove(rate);
            }
        }
        else if (rate is null)
        {
            _db.Set<JobOverheadRate>().Add(new JobOverheadRate(companyId, appliesTo, percent, costHead.Trim()));
        }
        else
        {
            rate.Percent = percent;
            rate.CostHead = costHead.Trim();
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<RetentionReleaseResult>> ReleaseRetentionAsync(long companyId, long contractId, decimal amount, DateOnly date, string retentionReceivableAccountCode, string documentNumber, CancellationToken ct = default)
        => _db.AtomicAsync(() => ReleaseRetentionCoreAsync(companyId, contractId, amount, date, retentionReceivableAccountCode, documentNumber, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<RetentionReleaseResult>> ReleaseRetentionCoreAsync(long companyId, long contractId, decimal amount, DateOnly date, string retentionReceivableAccountCode, string documentNumber, CancellationToken ct)
    {
        if (amount <= 0m)
        {
            return Error.Rule("Job.RetentionNonPositive", "The retention amount to release must be positive.");
        }

        JobContract? contract = await _db.Set<JobContract>()
            .FirstOrDefaultAsync(c => c.Id == contractId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (contract is null)
        {
            return Error.NotFound("Job.ContractNotFound", $"Contract {contractId} not found.");
        }

        if (amount > contract.RetentionHeld)
        {
            return Error.Rule("Job.RetentionExceedsHeld", $"The release {amount:0.00} exceeds the retention held {contract.RetentionHeld:0.00}.");
        }

        Company company = await _db.Set<Company>().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);

        // Move the retention from the holding receivable onto the client's AR so it ages and can be collected:
        // an AR invoice whose revenue side is the retention-receivable account (Dr AR control / Cr retention receivable).
        Result<TradingPostingResult> ar = await _salesLedger.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId,
            CustomerId = contract.CustomerId,
            DocumentNumber = documentNumber,
            DocumentDate = date,
            Currency = company.BaseCurrency,
            NetAmount = amount,
            TaxAmount = 0m,
            SalesAccountCode = retentionReceivableAccountCode,
            Narrative = $"Retention release — {contract.ContractNumber}",
        }, ct).ConfigureAwait(false);

        if (ar.IsFailure)
        {
            return ar.Error;
        }

        contract.ReleaseRetention(amount);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new RetentionReleaseResult(ar.Value.OpenItemId, ar.Value.JournalNumber, amount);
    }

    /// <inheritdoc />
    public Task<Result<RevenueRecognitionResult>> RecogniseRevenueAsync(long companyId, long contractId, DateOnly date, string contractAssetAccountCode, string contractLiabilityAccountCode, string documentNumber, CancellationToken ct = default)
        => _db.AtomicAsync(() => RecogniseRevenueCoreAsync(companyId, contractId, date, contractAssetAccountCode, contractLiabilityAccountCode, documentNumber, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<RevenueRecognitionResult>> RecogniseRevenueCoreAsync(long companyId, long contractId, DateOnly date, string contractAssetAccountCode, string contractLiabilityAccountCode, string documentNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(contractAssetAccountCode) || string.IsNullOrWhiteSpace(contractLiabilityAccountCode))
        {
            return Error.Validation("Job.PocAccounts", "Both the contract asset and contract liability accounts are required.");
        }

        JobContract? contract = await _db.Set<JobContract>().Include(c => c.Costs)
            .FirstOrDefaultAsync(c => c.Id == contractId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (contract is null)
        {
            return Error.NotFound("Job.ContractNotFound", $"Contract {contractId} not found.");
        }

        PocCalculation poc = PercentageOfCompletion.Compute(contract.ContractValue, contract.BudgetCost, contract.ActualCost, contract.CertifiedToDate, contract.RevenueAdjustmentToDate);
        if (poc.Movement == 0m)
        {
            return new RevenueRecognitionResult(poc.PercentComplete, poc.EarnedRevenue, poc.CertifiedToDate, poc.CumulativeAdjustment, 0m, null);
        }

        Company company = await _db.Set<Company>().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);

        // Unwind the prior position, then book the new one; revenue takes the net movement.
        List<JournalLineRequest> lines = [];
        decimal prior = poc.PriorAdjustment;
        if (prior > 0m)
        {
            lines.Add(Line(contract.RevenueAccountCode, JournalSide.Debit, prior));
            lines.Add(Line(contractAssetAccountCode, JournalSide.Credit, prior));
        }
        else if (prior < 0m)
        {
            lines.Add(Line(contractLiabilityAccountCode, JournalSide.Debit, -prior));
            lines.Add(Line(contract.RevenueAccountCode, JournalSide.Credit, -prior));
        }

        decimal target = poc.CumulativeAdjustment;
        if (target > 0m)
        {
            lines.Add(Line(contractAssetAccountCode, JournalSide.Debit, target));
            lines.Add(Line(contract.RevenueAccountCode, JournalSide.Credit, target));
        }
        else if (target < 0m)
        {
            lines.Add(Line(contract.RevenueAccountCode, JournalSide.Debit, -target));
            lines.Add(Line(contractLiabilityAccountCode, JournalSide.Credit, -target));
        }

        Result<PostingReceipt> posting = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "POC",
            DocumentNumber = documentNumber,
            Reference = contract.ContractNumber,
            Narrative = $"Revenue recognition {poc.PercentComplete:0.00}% complete — {contract.ContractNumber}",
            Lines = lines,
        }, ct).ConfigureAwait(false);
        if (posting.IsFailure)
        {
            return posting.Error;
        }

        contract.SetRevenueAdjustment(target);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new RevenueRecognitionResult(poc.PercentComplete, poc.EarnedRevenue, poc.CertifiedToDate, target, poc.Movement, posting.Value.JournalNumber);
    }

    /// <inheritdoc />
    public async Task<Result> ReviseBudgetAsync(long companyId, long contractId, decimal newBudgetCost, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("Job.ReasonRequired", "A reason is required to revise a contract budget."));
        }

        JobContract? contract = await _db.Set<JobContract>().Include(c => c.Costs)
            .FirstOrDefaultAsync(c => c.Id == contractId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (contract is null)
        {
            return Result.Failure(Error.NotFound("Job.ContractNotFound", $"Contract {contractId} not found."));
        }

        Result revised = contract.ReviseBudget(newBudgetCost);
        if (revised.IsFailure)
        {
            return revised;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<ExpectedLossResult>> ProvideForExpectedLossAsync(long companyId, long contractId, DateOnly date, string lossExpenseAccountCode, string provisionAccountCode, string documentNumber, CancellationToken ct = default)
        => _db.AtomicAsync(() => ProvideForExpectedLossCoreAsync(companyId, contractId, date, lossExpenseAccountCode, provisionAccountCode, documentNumber, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<ExpectedLossResult>> ProvideForExpectedLossCoreAsync(long companyId, long contractId, DateOnly date, string lossExpenseAccountCode, string provisionAccountCode, string documentNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(lossExpenseAccountCode) || string.IsNullOrWhiteSpace(provisionAccountCode))
        {
            return Error.Validation("Job.LossAccounts", "Both the expected-loss expense and provision accounts are required.");
        }

        JobContract? contract = await _db.Set<JobContract>()
            .FirstOrDefaultAsync(c => c.Id == contractId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (contract is null)
        {
            return Error.NotFound("Job.ContractNotFound", $"Contract {contractId} not found.");
        }

        ExpectedLossCalculation loss = ExpectedLoss.Compute(contract.ContractValue, contract.BudgetCost, contract.LossProvisionToDate);
        if (loss.Movement == 0m)
        {
            return new ExpectedLossResult(loss.ExpectedLoss, loss.PriorProvision, 0m, null);
        }

        Company company = await _db.Set<Company>().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);

        // Top-up: Dr loss expense / Cr provision. Release: the reverse.
        decimal amount = Math.Abs(loss.Movement);
        bool topUp = loss.Movement > 0m;
        Result<PostingReceipt> posting = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "ONEROUS",
            DocumentNumber = documentNumber,
            Reference = contract.ContractNumber,
            Narrative = (topUp ? "Expected loss provision" : "Expected loss provision release") + $" — {contract.ContractNumber}",
            Lines =
            [
                Line(topUp ? lossExpenseAccountCode : provisionAccountCode, JournalSide.Debit, amount),
                Line(topUp ? provisionAccountCode : lossExpenseAccountCode, JournalSide.Credit, amount),
            ],
        }, ct).ConfigureAwait(false);
        if (posting.IsFailure)
        {
            return posting.Error;
        }

        contract.SetLossProvision(loss.ExpectedLoss);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new ExpectedLossResult(loss.ExpectedLoss, loss.PriorProvision, loss.Movement, posting.Value.JournalNumber);
    }

    private static JournalLineRequest Line(string account, JournalSide side, decimal amount) =>
        new() { AccountCode = account, Side = side, TransactionAmount = amount };
}
