using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Waiting-list fees and transfer duty (brief §14 H27, H63).</summary>
public sealed class HousingFeeService : IHousingFeeService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The posting engine.</param>
    public HousingFeeService(One4AllDbContext db, IPostingEngine posting)
    {
        _db = db;
        _posting = posting;
    }

    /// <inheritdoc />
    public Task<Result<string>> RecordApplicantFeeAsync(ApplicantFeeReceipt receipt, CancellationToken ct = default)
        => _db.AtomicAsync(() => RecordApplicantFeeCoreAsync(receipt, ct), ct);

    private async Task<Result<string>> RecordApplicantFeeCoreAsync(ApplicantFeeReceipt receipt, CancellationToken ct)
    {
        if (!Enum.IsDefined(receipt.FeeType) || receipt.Amount <= 0m || string.IsNullOrWhiteSpace(receipt.ReceiptReference))
        {
            return Error.Validation("Hse.FeeDetails", "A fee needs its type, a positive amount and the receipt.");
        }

        HousingApplicant? applicant = await _db.HousingApplicants.AsNoTracking()
            .FirstOrDefaultAsync(a => a.CompanyId == receipt.CompanyId && a.WaitingListNumber == receipt.WaitingListNumber.Trim(), ct).ConfigureAwait(false);
        if (applicant is null)
        {
            return Error.NotFound("Hse.ApplicantNotFound", $"Applicant {receipt.WaitingListNumber} not found.");
        }

        if (receipt.FeeType == ApplicantFeeType.Registration
            && await _db.Set<ApplicantFee>().AnyAsync(f => f.ApplicantId == applicant.Id && f.FeeType == ApplicantFeeType.Registration, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hse.FeeAlreadyPaid", $"Applicant {receipt.WaitingListNumber} has already paid the registration fee.");
        }

        Result<string> journal = await PostAsync(receipt.CompanyId, receipt.Date, "HSE-FEE", receipt.ReceiptReference.Trim(),
            $"{receipt.FeeType} fee {receipt.WaitingListNumber}", receipt.BankAccountCode, receipt.IncomeAccountCode, receipt.Amount, ct).ConfigureAwait(false);
        if (journal.IsFailure)
        {
            return journal.Error;
        }

        _db.Set<ApplicantFee>().Add(new ApplicantFee(receipt.CompanyId, applicant.Id, receipt.FeeType, receipt.Amount, receipt.Date, receipt.ReceiptReference.Trim(), journal.Value));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return journal.Value;
    }

    /// <inheritdoc />
    public Task<Result<decimal>> WithholdTransferDutyAsync(TransferDutyReceipt receipt, CancellationToken ct = default)
        => _db.AtomicAsync(() => WithholdCoreAsync(receipt, ct), ct);

    private async Task<Result<decimal>> WithholdCoreAsync(TransferDutyReceipt receipt, CancellationToken ct)
    {
        if (receipt.RatePercent is <= 0m or > 100m || string.IsNullOrWhiteSpace(receipt.ReceiptReference) || string.IsNullOrWhiteSpace(receipt.LiabilityAccountCode))
        {
            return Error.Validation("Hse.DutyDetails", "Duty needs a rate from 0 to 100%, the receipt and the liability account.");
        }

        TransferApplication? transfer = await _db.Set<TransferApplication>().AsNoTracking()
            .FirstOrDefaultAsync(t => t.CompanyId == receipt.CompanyId && t.ApplicationNumber == receipt.ApplicationNumber.Trim(), ct).ConfigureAwait(false);
        if (transfer is null)
        {
            return Error.NotFound("Hse.TransferNotFound", $"Transfer {receipt.ApplicationNumber} not found.");
        }

        if (transfer.Consideration <= 0m)
        {
            return Error.Rule("Hse.NoConsideration", $"Transfer {transfer.ApplicationNumber} has no consideration to assess duty on.");
        }

        if (await _db.Set<TransferDuty>().AnyAsync(d => d.TransferApplicationId == transfer.Id, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hse.DutyAlreadyWithheld", $"Duty on transfer {transfer.ApplicationNumber} has already been received.");
        }

        decimal duty = Math.Round(transfer.Consideration * receipt.RatePercent / 100m, 2, MidpointRounding.AwayFromZero);
        Result<string> journal = await PostAsync(receipt.CompanyId, receipt.Date, "HSE-DUTY", receipt.ReceiptReference.Trim(), $"Transfer duty {transfer.ApplicationNumber}",
            receipt.BankAccountCode, receipt.LiabilityAccountCode, duty, ct).ConfigureAwait(false);
        if (journal.IsFailure)
        {
            return journal.Error;
        }

        _db.Set<TransferDuty>().Add(new TransferDuty(receipt.CompanyId, transfer.Id, transfer.Consideration, receipt.RatePercent, duty, receipt.Date, receipt.ReceiptReference.Trim(),
            receipt.LiabilityAccountCode, journal.Value));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return duty;
    }

    /// <inheritdoc />
    public Task<Result<string>> RemitTransferDutyAsync(long companyId, string applicationNumber, DateOnly date, string reference, string bankAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => RemitCoreAsync(companyId, applicationNumber, date, reference, bankAccountCode, ct), ct);

    private async Task<Result<string>> RemitCoreAsync(long companyId, string applicationNumber, DateOnly date, string reference, string bankAccountCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return Error.Validation("Hse.RemittanceReference", "Record the revenue authority's acknowledgement.");
        }

        TransferDuty? duty = await (from d in _db.Set<TransferDuty>() join t in _db.Set<TransferApplication>() on d.TransferApplicationId equals t.Id
                                    where d.CompanyId == companyId && t.ApplicationNumber == applicationNumber.Trim() select d).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (duty is null)
        {
            return Error.NotFound("Hse.DutyNotFound", $"No duty has been received on transfer {applicationNumber}.");
        }

        if (duty.RemittedOn is not null)
        {
            return Error.Conflict("Hse.DutyRemitted", $"Duty on transfer {applicationNumber} was remitted on {duty.RemittedOn:yyyy-MM-dd}.");
        }

        if (date < duty.WithheldOn)
        {
            return Error.Validation("Hse.RemittanceDate", "Duty cannot be remitted before it was received.");
        }

        Result<string> journal = await PostAsync(companyId, date, "HSE-DUTYREM", reference.Trim(), $"Transfer duty {applicationNumber} remitted",
            duty.LiabilityAccountCode, bankAccountCode, duty.Amount, ct).ConfigureAwait(false);
        if (journal.IsFailure)
        {
            return journal.Error;
        }

        duty.Remit(date, reference.Trim(), journal.Value);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return journal.Value;
    }

    private async Task<Result<string>> PostAsync(long companyId, DateOnly date, string documentType, string documentNumber, string narrative, string debitAccount, string creditAccount, decimal amount, CancellationToken ct)
    {
        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Housing.CompanyNotFound", $"Company {companyId} not found.");
        }

        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = documentType,
            DocumentNumber = documentNumber,
            Narrative = narrative,
            Lines =
            [
                new JournalLineRequest { AccountCode = debitAccount, Side = JournalSide.Debit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = creditAccount, Side = JournalSide.Credit, TransactionAmount = amount },
            ],
        }, ct).ConfigureAwait(false);
        return posted.IsFailure ? posted.Error : posted.Value.JournalNumber;
    }
}
