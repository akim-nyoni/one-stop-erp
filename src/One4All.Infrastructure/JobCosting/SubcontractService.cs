using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.JobCosting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.JobCosting;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.JobCosting;

/// <summary>Certifies subcontracts through the supplier ledger with retention, and logs plant hours and running costs.</summary>
public sealed class SubcontractService : ISubcontractService
{
    private readonly One4AllDbContext _db;
    private readonly IPurchaseLedgerService _payables;
    private readonly IPostingEngine _postingEngine;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="payables">Invoices the subcontractor.</param>
    /// <param name="postingEngine">Posts held retention.</param>
    public SubcontractService(One4AllDbContext db, IPurchaseLedgerService payables, IPostingEngine postingEngine)
    {
        _db = db;
        _payables = payables;
        _postingEngine = postingEngine;
    }

    /// <inheritdoc />
    public async Task<Result<long>> LetAsync(long companyId, long contractId, long supplierId, string reference, string description, decimal value, decimal retentionPercent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(description) || value <= 0m || retentionPercent is < 0m or > 100m)
        {
            return Error.Validation("Job.SubcontractInvalid", "A subcontract needs a reference, a package, a positive value and a retention between 0 and 100%.");
        }

        if (!await _db.JobContracts.AnyAsync(c => c.Id == contractId && c.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Job.ContractNotFound", $"Contract {contractId} not found.");
        }

        if (!await _db.Suppliers.AnyAsync(s => s.Id == supplierId && s.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Job.SupplierNotFound", $"Supplier {supplierId} not found.");
        }

        string normalised = reference.Trim().ToUpperInvariant();
        if (await _db.Subcontracts.AnyAsync(s => s.CompanyId == companyId && s.Reference == normalised, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Job.SubcontractExists", $"Subcontract {normalised} already exists.");
        }

        Subcontract subcontract = new(companyId, contractId, supplierId, normalised, description.Trim(), value, retentionPercent);
        _db.Subcontracts.Add(subcontract);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return subcontract.Id;
    }

    /// <inheritdoc />
    public Task<Result<SubcontractCertificateResult>> CertifyAsync(SubcontractCertificateRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => CertifyCoreAsync(request, ct), ct);

    // One unit of work: the supplier invoice, the retention journal, the job cost and the certificate commit together.
    private async Task<Result<SubcontractCertificateResult>> CertifyCoreAsync(SubcontractCertificateRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Number) || string.IsNullOrWhiteSpace(request.CostAccountCode) || string.IsNullOrWhiteSpace(request.RetentionPayableAccountCode))
        {
            return Error.Validation("Job.CertificateInvalid", "A certificate needs a number, a job cost account and a retention payable account.");
        }

        Subcontract? subcontract = await _db.Subcontracts.Include(s => s.Certificates).FirstOrDefaultAsync(s => s.Id == request.SubcontractId && s.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (subcontract is null)
        {
            return Error.NotFound("Job.SubcontractNotFound", $"Subcontract {request.SubcontractId} not found.");
        }

        if (request.WorkDoneToDate <= subcontract.CertifiedToDate)
        {
            return Error.Rule("Job.CertificateNoProgress", $"Work to date must exceed the {subcontract.CertifiedToDate:0.00} already certified.");
        }

        if (request.WorkDoneToDate > subcontract.Value)
        {
            return Error.Rule("Job.CertificateOverValue", $"Work to date cannot exceed the subcontract value {subcontract.Value:0.00}; vary the subcontract first.");
        }

        decimal gross = request.WorkDoneToDate - subcontract.CertifiedToDate;
        decimal retention = Math.Round(gross * subcontract.RetentionPercent / 100m, 2, MidpointRounding.AwayFromZero);
        decimal payable = gross - retention;
        CurrencyCode currency = await _db.Companies.Where(c => c.Id == request.CompanyId).Select(c => c.BaseCurrency).SingleAsync(ct).ConfigureAwait(false);
        string invoiceNumber = $"{subcontract.Reference}/{request.Number.Trim().ToUpperInvariant()}";
        if (payable > 0m)
        {
            Result<TradingPostingResult> invoice = await _payables.PostInvoiceAsync(new PurchaseInvoiceRequest
            {
                CompanyId = request.CompanyId, SupplierId = subcontract.SupplierId, DocumentNumber = invoiceNumber, DocumentDate = request.Date, Currency = currency, NetAmount = payable,
                ExpenseAccountCode = request.CostAccountCode, Narrative = $"Subcontract {subcontract.Reference} certificate {request.Number.Trim()}",
            }, ct).ConfigureAwait(false);
            if (invoice.IsFailure)
            {
                return invoice.Error;
            }
        }

        if (retention > 0m)
        {
            Result<PostingReceipt> held = await _postingEngine.PostAsync(new JournalRequest
            {
                CompanyId = request.CompanyId, SourceModule = "GL", PostingDate = request.Date, TransactionCurrency = currency, EnforceManualJournalRules = false,
                DocumentType = "SubcontractRetention", DocumentNumber = invoiceNumber, Narrative = $"Retention held on {invoiceNumber}",
                Lines =
                [
                    new JournalLineRequest { AccountCode = request.CostAccountCode, Side = JournalSide.Debit, TransactionAmount = retention },
                    new JournalLineRequest { AccountCode = request.RetentionPayableAccountCode, Side = JournalSide.Credit, TransactionAmount = retention },
                ],
            }, ct).ConfigureAwait(false);
            if (held.IsFailure)
            {
                return held.Error;
            }
        }

        JobContract contract = await _db.JobContracts.Include(c => c.Costs).SingleAsync(c => c.Id == subcontract.JobContractId, ct).ConfigureAwait(false);
        contract.CaptureCost("Subcontract", JobCostSource.Subcontractor, gross, request.Date, invoiceNumber);
        subcontract.AddCertificate(new SubcontractCertificate(request.Number.Trim().ToUpperInvariant(), request.Date, request.WorkDoneToDate, gross, retention, invoiceNumber));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new SubcontractCertificateResult(gross, retention, payable);
    }

    /// <inheritdoc />
    public Task<Result> ReleaseRetentionAsync(long companyId, long subcontractId, decimal amount, DateOnly date, string retentionPayableAccountCode, string documentNumber, CancellationToken ct = default)
        => _db.AtomicAsync(() => ReleaseCoreAsync(companyId, subcontractId, amount, date, retentionPayableAccountCode, documentNumber, ct), ct);

    // One unit of work: the supplier invoice for the released retention and the subcontract's release commit together.
    private async Task<Result> ReleaseCoreAsync(long companyId, long subcontractId, decimal amount, DateOnly date, string retentionPayableAccountCode, string documentNumber, CancellationToken ct)
    {
        Subcontract? subcontract = await _db.Subcontracts.Include(s => s.Certificates).FirstOrDefaultAsync(s => s.Id == subcontractId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (subcontract is null)
        {
            return Result.Failure(Error.NotFound("Job.SubcontractNotFound", $"Subcontract {subcontractId} not found."));
        }

        if (amount <= 0m || amount > subcontract.RetentionHeld)
        {
            return Result.Failure(Error.Rule("Job.RetentionRelease", $"Between 0 and the {subcontract.RetentionHeld:0.00} held can be released."));
        }

        CurrencyCode currency = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.BaseCurrency).SingleAsync(ct).ConfigureAwait(false);
        Result<TradingPostingResult> invoice = await _payables.PostInvoiceAsync(new PurchaseInvoiceRequest
        {
            CompanyId = companyId, SupplierId = subcontract.SupplierId, DocumentNumber = documentNumber, DocumentDate = date, Currency = currency, NetAmount = amount,
            ExpenseAccountCode = retentionPayableAccountCode, Narrative = $"Retention released on subcontract {subcontract.Reference}",
        }, ct).ConfigureAwait(false);
        if (invoice.IsFailure)
        {
            return Result.Failure(invoice.Error);
        }

        subcontract.Release(amount);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result> LogPlantHoursAsync(long companyId, long plantItemId, long contractId, DateOnly date, decimal hours, CancellationToken ct = default)
        => _db.AtomicAsync(() => LogHoursCoreAsync(companyId, plantItemId, contractId, date, hours, ct), ct);

    // One unit of work: the plant log and the job's plant cost commit together.
    private async Task<Result> LogHoursCoreAsync(long companyId, long plantItemId, long contractId, DateOnly date, decimal hours, CancellationToken ct)
    {
        if (hours <= 0m || hours > 24m)
        {
            return Result.Failure(Error.Validation("Job.PlantHours", "Plant hours must be more than 0 and at most 24 a day."));
        }

        PlantItem? plant = await _db.PlantItems.AsNoTracking().FirstOrDefaultAsync(p => p.Id == plantItemId && p.CompanyId == companyId && p.IsActive, ct).ConfigureAwait(false);
        if (plant is null)
        {
            return Result.Failure(Error.NotFound("Job.PlantNotFound", $"No plant in service with id {plantItemId}."));
        }

        decimal logged = await _db.PlantLogs.Where(l => l.PlantItemId == plantItemId && l.Date == date).SumAsync(l => l.Hours, ct).ConfigureAwait(false);
        if (logged + hours > 24m)
        {
            return Result.Failure(Error.Rule("Job.PlantOverbooked", $"{plant.Code} already has {logged:0.##} hours on {date:yyyy-MM-dd}."));
        }

        JobContract? contract = await _db.JobContracts.Include(c => c.Costs).FirstOrDefaultAsync(c => c.Id == contractId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (contract is null)
        {
            return Result.Failure(Error.NotFound("Job.ContractNotFound", $"Contract {contractId} not found."));
        }

        decimal charge = Math.Round(hours * plant.HireRatePerHour, 2, MidpointRounding.AwayFromZero);
        _db.PlantLogs.Add(new PlantLog(companyId, plantItemId, date, contractId, hours, charge, 0m, $"{hours:0.##} h on {contract.ContractNumber}"));
        contract.CaptureCost("Plant", JobCostSource.Plant, charge, date, plant.Code);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> LogPlantCostAsync(long companyId, long plantItemId, DateOnly date, decimal amount, string description, CancellationToken ct = default)
    {
        if (amount <= 0m || string.IsNullOrWhiteSpace(description))
        {
            return Result.Failure(Error.Validation("Job.PlantCost", "A running cost needs a positive amount and a description."));
        }

        if (!await _db.PlantItems.AnyAsync(p => p.Id == plantItemId && p.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.NotFound("Job.PlantNotFound", $"Plant {plantItemId} not found."));
        }

        _db.PlantLogs.Add(new PlantLog(companyId, plantItemId, date, null, 0m, 0m, amount, description.Trim()));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}
