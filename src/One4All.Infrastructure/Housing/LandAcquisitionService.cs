using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Abstractions.Security;
using One4All.Application.Abstractions.Workflow;
using One4All.Application.Housing;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.Security;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Land acquisitions: due diligence, cost build-up into land WIP, completion into the land bank.</summary>
public sealed class LandAcquisitionService : ILandAcquisitionService
{
    private const string AcquisitionPermission = "Housing.Acquisition";

    private readonly One4AllDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly IPostingEngine _posting;
    private readonly IPermissionService _permissions;
    private readonly Lazy<IWorkflowService>? _workflow;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    /// <param name="posting">The posting engine.</param>
    /// <param name="permissions">The permission resolver.</param>
    /// <param name="workflow">The approval workflow acquisitions go through when the council defines one.</param>
    public LandAcquisitionService(One4AllDbContext db, INumberSequenceService numbers, IPostingEngine posting, IPermissionService permissions, Lazy<IWorkflowService>? workflow = null)
    {
        _workflow = workflow;
        _db = db;
        _numbers = numbers;
        _posting = posting;
        _permissions = permissions;
    }

    /// <inheritdoc />
    public async Task<Result<string>> OpenAsync(AcquisitionCase acquisition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        if (string.IsNullOrWhiteSpace(acquisition.ParcelNumber) || string.IsNullOrWhiteSpace(acquisition.Description) || acquisition.AreaSquareMetres <= 0m
            || string.IsNullOrWhiteSpace(acquisition.Counterparty) || string.IsNullOrWhiteSpace(acquisition.ResolutionReference) || !Enum.IsDefined(acquisition.Method))
        {
            return Error.Validation("Hse.AcquisitionDetails", "An acquisition needs a parcel reference, description, area, method, counterparty and the authorising resolution.");
        }

        string parcelNumber = acquisition.ParcelNumber.Trim().ToUpperInvariant(); // as the land bank stores it
        if (await _db.LandParcels.AnyAsync(p => p.CompanyId == acquisition.CompanyId && p.ParcelNumber == parcelNumber, ct).ConfigureAwait(false)
            || await _db.Set<LandAcquisition>().AnyAsync(a => a.CompanyId == acquisition.CompanyId && a.ParcelNumber == parcelNumber && a.Status == AcquisitionStatus.Open, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hse.ParcelExists", $"Parcel {parcelNumber} is already in the land bank or being acquired.");
        }

        Result<string> number = await _numbers.AllocateAsync(acquisition.CompanyId, "ACQ", acquisition.Date, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        _db.Set<LandAcquisition>().Add(new LandAcquisition(acquisition.CompanyId, number.Value, parcelNumber, acquisition.Description.Trim(), acquisition.AreaSquareMetres,
            acquisition.Method, acquisition.Counterparty.Trim(), acquisition.ResolutionReference.Trim(), acquisition.Date));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return number.Value;
    }

    /// <inheritdoc />
    public async Task<Result> RecordValuationAsync(long companyId, string acquisitionNumber, string valuer, DateOnly valuedOn, decimal value, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(valuer) || value < 0m)
        {
            return Result.Failure(Error.Validation("Hse.Valuation", "A valuation needs the valuer and a value of zero or more."));
        }

        Result<LandAcquisition> found = await OpenCaseAsync(companyId, acquisitionNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        found.Value.Value(valuer.Trim(), valuedOn, value);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SignOffAsync(long companyId, string acquisitionNumber, DueDiligenceItem item, string finding, bool acceptable, long userId, DateOnly date, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(item) || string.IsNullOrWhiteSpace(finding))
        {
            return Result.Failure(Error.Validation("Hse.DueDiligence", "A sign-off needs the item and what was found."));
        }

        if (!(await _permissions.ResolveAsync(userId, ct).ConfigureAwait(false)).Has(AcquisitionPermission, OperationFlags.Edit))
        {
            return Result.Failure(Error.Rule("Hse.NotAuthorised", "Only an officer with the acquisition permission can sign off due diligence."));
        }

        Result<LandAcquisition> found = await OpenCaseAsync(companyId, acquisitionNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        found.Value.SignOff(item, finding.Trim(), acceptable, userId, date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<string>> AddCostAsync(long companyId, string acquisitionNumber, AcquisitionCostInput cost, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cost);
        return _db.AtomicAsync(() => AddCostCoreAsync(companyId, acquisitionNumber, cost, ct), ct);
    }

    private async Task<Result<string>> AddCostCoreAsync(long companyId, string acquisitionNumber, AcquisitionCostInput cost, CancellationToken ct)
    {
        if (cost.Amount <= 0m || !Enum.IsDefined(cost.Component) || string.IsNullOrWhiteSpace(cost.Reference)
            || string.IsNullOrWhiteSpace(cost.WipAccountCode) || string.IsNullOrWhiteSpace(cost.FundingAccountCode))
        {
            return Error.Validation("Hse.AcquisitionCost", "A cost needs its element, a positive amount, a reference, and the WIP and funding accounts.");
        }

        Result<LandAcquisition> found = await OpenCaseAsync(companyId, acquisitionNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        LandAcquisition acquisition = found.Value;
        Result<PostingReceipt> posted = await PostAsync(companyId, cost.Date, "LAND-COST", $"{acquisition.AcquisitionNumber}-{acquisition.Costs.Count + 1}",
            $"{acquisition.AcquisitionNumber} {cost.Component} ({cost.Reference.Trim()})", cost.WipAccountCode, cost.FundingAccountCode, cost.Amount, ct).ConfigureAwait(false);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        acquisition.AddCost(new AcquisitionCost(cost.Component, cost.Amount, cost.Date, cost.Reference.Trim(), posted.Value.JournalNumber));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return posted.Value.JournalNumber;
    }

    /// <inheritdoc />
    public async Task<Result<AcquisitionPosition>> PositionAsync(long companyId, string acquisitionNumber, CancellationToken ct = default)
    {
        Result<LandAcquisition> found = await FindAsync(companyId, acquisitionNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        LandAcquisition acquisition = found.Value;
        IReadOnlyList<CostElement> buildUp = AcquisitionReadiness.BuildUp(acquisition.Costs.Select(c => new CostElement(c.Component, c.Amount)));
        IReadOnlyList<string> outstanding = acquisition.Status == AcquisitionStatus.Open ? Problems(acquisition, buildUp) : [];
        return new AcquisitionPosition(acquisition.AcquisitionNumber, acquisition.Status, buildUp, acquisition.TotalCost, outstanding);
    }

    /// <inheritdoc />
    public async Task<Result<int>> OfferAsync(long companyId, string acquisitionNumber, NegotiationParty party, decimal amount, DateOnly date, string? note, CancellationToken ct = default)
    {
        if (amount <= 0m)
        {
            return Error.Validation("Hse.OfferAmount", "An offer must be more than zero.");
        }

        Result<LandAcquisition> found = await OpenCaseAsync(companyId, acquisitionNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        NegotiationOffer? offer = found.Value.Offer(party, amount, date, string.IsNullOrWhiteSpace(note) ? null : note.Trim());
        if (offer is null)
        {
            return Error.Rule("Hse.NegotiationClosed", $"The negotiation on {found.Value.AcquisitionNumber} is {found.Value.Negotiation}; no more offers are taken.");
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return offer.Sequence;
    }

    /// <inheritdoc />
    public async Task<Result<decimal>> AgreePriceAsync(long companyId, string acquisitionNumber, DateOnly date, CancellationToken ct = default)
    {
        Result<LandAcquisition> found = await OpenCaseAsync(companyId, acquisitionNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        decimal? agreed = found.Value.Agree(date);
        if (agreed is null)
        {
            return Error.Rule("Hse.NoOffer", "There is no open offer to accept: record the offer first (the latest one is the price agreed).");
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return agreed.Value;
    }

    /// <inheritdoc />
    public async Task<Result> BreakOffAsync(long companyId, string acquisitionNumber, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("Hse.BreakOffReason", "Give the reason the talks failed."));
        }

        Result<LandAcquisition> found = await OpenCaseAsync(companyId, acquisitionNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        found.Value.BreakOff(reason.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private Task<bool> GovernedAsync(long companyId, CancellationToken ct)
        => _db.WorkflowDefinitions.AnyAsync(d => d.CompanyId == companyId && d.DocumentType == WorkflowDocumentTypes.LandAcquisition && d.IsActive, ct);

    /// <inheritdoc />
    public Task<Result<decimal>> SubmitForApprovalAsync(long companyId, string acquisitionNumber, long userId, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            Result<LandAcquisition> found = await OpenCaseAsync(companyId, acquisitionNumber, ct).ConfigureAwait(false);
            if (found.IsFailure)
            {
                return found.Error;
            }

            LandAcquisition acquisition = found.Value;
            if (_workflow is null || !await GovernedAsync(companyId, ct).ConfigureAwait(false))
            {
                return Error.Rule("Hse.NoAcquisitionWorkflow", "No land acquisition workflow is defined: acquisitions complete without one.");
            }

            if (acquisition.Approval is AcquisitionApproval.Pending or AcquisitionApproval.Approved)
            {
                return Error.Rule("Hse.AcquisitionSubmitted", $"{acquisition.AcquisitionNumber} is already {acquisition.Approval.ToString().ToLowerInvariant()}.");
            }

            decimal value = acquisition.AgreedPrice ?? acquisition.TotalCost;
            Result<bool> started = await Workflow.WorkflowGate.StartIfDefinedAsync(_db, _workflow, companyId, WorkflowDocumentTypes.LandAcquisition, acquisition.AcquisitionNumber, value, userId,
                [("Method", acquisition.Method.ToString()), ("Parcel", acquisition.ParcelNumber)], ct).ConfigureAwait(false);
            if (started.IsFailure)
            {
                return started.Error;
            }

            acquisition.Approval = AcquisitionApproval.Pending;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<decimal>.Success(value);
        }, ct);

    /// <summary>Applies the end of an acquisition's workflow: approved lets it complete; rejected sends it back.</summary>
    internal async Task<Result> ConcludeAsync(long companyId, string reference, bool approved, CancellationToken ct)
    {
        LandAcquisition? acquisition = await _db.Set<LandAcquisition>().FirstOrDefaultAsync(a => a.CompanyId == companyId && a.AcquisitionNumber == reference, ct).ConfigureAwait(false);
        if (acquisition is null)
        {
            return Result.Failure(Error.NotFound("Hse.AcquisitionNotFound", $"No acquisition {reference}."));
        }

        acquisition.Approval = approved ? AcquisitionApproval.Approved : AcquisitionApproval.Rejected;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<long>> CompleteAsync(long companyId, string acquisitionNumber, DateOnly date, CancellationToken ct = default)
        => _db.AtomicAsync(() => CompleteCoreAsync(companyId, acquisitionNumber, date, ct), ct);

    private async Task<Result<long>> CompleteCoreAsync(long companyId, string acquisitionNumber, DateOnly date, CancellationToken ct)
    {
        Result<LandAcquisition> found = await OpenCaseAsync(companyId, acquisitionNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        LandAcquisition acquisition = found.Value;
        IReadOnlyList<string> problems = Problems(acquisition, AcquisitionReadiness.BuildUp(acquisition.Costs.Select(c => new CostElement(c.Component, c.Amount))));
        if (problems.Count > 0)
        {
            return Error.Rule("Hse.DueDiligenceIncomplete", string.Join(" ", problems));
        }

        // Under an acquisition workflow the council's bands must have approved it first.
        if (acquisition.Approval != AcquisitionApproval.Approved && await GovernedAsync(companyId, ct).ConfigureAwait(false))
        {
            return Error.Rule("Hse.AcquisitionApproval", acquisition.Approval switch
            {
                AcquisitionApproval.Pending => $"{acquisition.AcquisitionNumber} is waiting for approval.",
                AcquisitionApproval.Rejected => $"{acquisition.AcquisitionNumber} was rejected: renegotiate and submit it again, or abandon it.",
                _ => $"{acquisition.AcquisitionNumber} needs approval: submit it for approval first.",
            });
        }

        // The costs are already in land WIP; the parcel simply carries their total.
        LandParcel parcel = new(companyId, acquisition.ParcelNumber, acquisition.Description, acquisition.AreaSquareMetres, acquisition.TotalCost);
        _db.LandParcels.Add(parcel);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        acquisition.Complete(parcel.Id, date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return parcel.Id;
    }

    /// <inheritdoc />
    public Task<Result<decimal>> AbandonAsync(long companyId, string acquisitionNumber, DateOnly date, string reason, string expenseAccountCode, string wipAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => AbandonCoreAsync(companyId, acquisitionNumber, date, reason, expenseAccountCode, wipAccountCode, ct), ct);

    private async Task<Result<decimal>> AbandonCoreAsync(long companyId, string acquisitionNumber, DateOnly date, string reason, string expenseAccountCode, string wipAccountCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("Hse.AbandonReason", "Give the reason for abandoning the acquisition.");
        }

        Result<LandAcquisition> found = await OpenCaseAsync(companyId, acquisitionNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        LandAcquisition acquisition = found.Value;
        decimal total = acquisition.TotalCost;
        if (total > 0m)
        {
            Result<PostingReceipt> posted = await PostAsync(companyId, date, "LAND-ABANDON", $"{acquisition.AcquisitionNumber}-W",
                $"{acquisition.AcquisitionNumber} abandoned: {reason.Trim()}", expenseAccountCode, wipAccountCode, total, ct).ConfigureAwait(false);
            if (posted.IsFailure)
            {
                return posted.Error;
            }
        }

        acquisition.Abandon(date, reason.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return total;
    }

    // The readiness rules, plus the negotiation: once offers have been made a purchase cannot complete until the price is agreed (or if talks broke off).
    private static IReadOnlyList<string> Problems(LandAcquisition acquisition, IReadOnlyList<CostElement> buildUp)
    {
        List<string> problems = [.. AcquisitionReadiness.Problems(acquisition.Method, [.. acquisition.Checks.Select(c => new SignedOffItem(c.Item, c.Acceptable))], acquisition.ValuedAt is not null, buildUp)];
        if (acquisition.Negotiation == NegotiationStatus.Open)
        {
            problems.Add($"Price not agreed: {acquisition.Offers.Count} offer(s) on the table, the latest {acquisition.Offers[^1].Party} at {acquisition.Offers[^1].Amount:N2}.");
        }
        else if (acquisition.Negotiation == NegotiationStatus.BrokenOff)
        {
            problems.Add($"Negotiation broken off: {acquisition.BreakOffReason}.");
        }

        return problems;
    }

    private async Task<Result<PostingReceipt>> PostAsync(long companyId, DateOnly date, string documentType, string documentNumber, string narrative, string debitAccount, string creditAccount, decimal amount, CancellationToken ct)
    {
        CurrencyCode currency = await _db.Set<Company>().Where(c => c.Id == companyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false);
        return await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = currency, DocumentType = documentType, DocumentNumber = documentNumber, Narrative = narrative,
            Lines =
            [
                new JournalLineRequest { AccountCode = debitAccount.Trim(), Side = JournalSide.Debit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = creditAccount.Trim(), Side = JournalSide.Credit, TransactionAmount = amount },
            ],
        }, ct).ConfigureAwait(false);
    }

    private async Task<Result<LandAcquisition>> OpenCaseAsync(long companyId, string acquisitionNumber, CancellationToken ct)
    {
        Result<LandAcquisition> found = await FindAsync(companyId, acquisitionNumber, ct).ConfigureAwait(false);
        if (found.IsSuccess && found.Value.Status != AcquisitionStatus.Open)
        {
            return Error.Rule("Hse.AcquisitionClosed", $"{found.Value.AcquisitionNumber} is {found.Value.Status}.");
        }

        return found;
    }

    private async Task<Result<LandAcquisition>> FindAsync(long companyId, string acquisitionNumber, CancellationToken ct)
    {
        string number = (acquisitionNumber ?? string.Empty).Trim();
        LandAcquisition? acquisition = await _db.Set<LandAcquisition>().Include(a => a.Checks).Include(a => a.Costs)
            .FirstOrDefaultAsync(a => a.CompanyId == companyId && a.AcquisitionNumber == number, ct).ConfigureAwait(false);
        if (acquisition is null)
        {
            return Error.NotFound("Hse.AcquisitionNotFound", $"Acquisition {number} not found.");
        }

        return acquisition;
    }
}

/// <summary>Applies the end of a land acquisition's workflow.</summary>
internal sealed class LandAcquisitionWorkflowHandler : IWorkflowDocumentHandler
{
    private readonly LandAcquisitionService _acquisitions;

    public LandAcquisitionWorkflowHandler(LandAcquisitionService acquisitions) => _acquisitions = acquisitions;

    public string DocumentType => WorkflowDocumentTypes.LandAcquisition;

    public Task<Result> ConcludeAsync(long companyId, string documentReference, Core.Workflow.WorkflowStatus outcome, long decidedByUserId, string? comment, CancellationToken ct = default)
        => _acquisitions.ConcludeAsync(companyId, documentReference, outcome == Core.Workflow.WorkflowStatus.Approved, ct);
}
