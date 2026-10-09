using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Abstractions.Security;
using One4All.Application.Abstractions.Workflow;
using One4All.Application.Housing;
using One4All.Application.Municipal;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.Municipal;
using One4All.Core.Security;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Change of ownership applications: checklist, live checks, dual approval and atomic completion.</summary>
public sealed class TransferApplicationService : ITransferApplicationService
{
    private const string ChangePermission = "Housing.OwnershipChange";
    private const string ApprovePermission = "Housing.OwnershipApprove";

    private readonly One4AllDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly IPermissionService _permissions;
    private readonly IHousingService _housing;
    private readonly IOwnershipService _ownership;
    private readonly Lazy<IWorkflowService>? _workflow;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    /// <param name="permissions">The permission resolver.</param>
    /// <param name="housing">The stand register.</param>
    /// <param name="ownership">The municipal owner register.</param>
    public TransferApplicationService(One4AllDbContext db, INumberSequenceService numbers, IPermissionService permissions, IHousingService housing, IOwnershipService ownership,
        Lazy<IWorkflowService>? workflow = null)
    {
        _workflow = workflow;
        _db = db;
        _numbers = numbers;
        _permissions = permissions;
        _housing = housing;
        _ownership = ownership;
    }

    /// <inheritdoc />
    public async Task<Result<string>> LodgeAsync(TransferLodgement lodgement, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lodgement);
        if (!Enum.IsDefined(lodgement.TransferType) || lodgement.Consideration < 0m)
        {
            return Error.Validation("Hse.TransferDetails", "A transfer needs its type and no negative consideration.");
        }

        HousingStand? stand = await _db.HousingStands.AsNoTracking().FirstOrDefaultAsync(s => s.Id == lodgement.StandId && s.CompanyId == lodgement.CompanyId, ct).ConfigureAwait(false);
        if (stand is null)
        {
            return Error.NotFound("Hse.StandNotFound", $"Stand {lodgement.StandId} not found.");
        }

        if (stand.CurrentHolderCustomerId is not long holderId)
        {
            return Error.Rule("Hse.NoHolder", $"Stand {stand.StandNumber} has no holder to transfer from.");
        }

        bool sameHolder = lodgement.ToCustomerId == holderId;
        if (sameHolder != lodgement.TransferType is TransferType.ChangeOfName or TransferType.Correction)
        {
            return Error.Validation("Hse.TransferParties", "Only a change of name or a correction keeps the same holder; every other transfer needs a new one.");
        }

        if (!await _db.Customers.AnyAsync(c => c.Id == lodgement.ToCustomerId && c.CompanyId == lodgement.CompanyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Hse.TransfereeNotFound", $"Customer {lodgement.ToCustomerId} not found.");
        }

        if (await StandClaims.AnyBlockedAsync(_db, [stand.Id], ct).ConfigureAwait(false))
        {
            return Error.Rule("Hse.StandBlocked", $"Stand {stand.StandNumber} is blocked by a caveat or an open land dispute and cannot be transferred.");
        }

        if (await _db.Set<TransferApplication>().AnyAsync(a => a.StandId == stand.Id && a.Status != TransferStatus.Completed && a.Status != TransferStatus.Rejected, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hse.TransferOpen", $"Stand {stand.StandNumber} already has a transfer application open.");
        }

        Result<string> number = await _numbers.AllocateAsync(lodgement.CompanyId, "TRF", lodgement.Date, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        _db.Set<TransferApplication>().Add(new TransferApplication(lodgement.CompanyId, number.Value, stand.Id, lodgement.TransferType, holderId, lodgement.ToCustomerId, lodgement.Consideration, lodgement.Date));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return number.Value;
    }

    /// <inheritdoc />
    public async Task<Result> VerifyAsync(long companyId, string applicationNumber, TransferRequirement requirement, string documentReference, long userId, DateOnly date, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(documentReference))
        {
            return Result.Failure(Error.Validation("Hse.TransferDocument", "Name the document verified."));
        }

        if (!await AllowedAsync(userId, ChangePermission, OperationFlags.Edit, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Rule("Hse.NotAuthorised", "Only an officer with the ownership-change permission can verify a transfer."));
        }

        Result<TransferApplication> found = await FindAsync(companyId, applicationNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        TransferApplication application = found.Value;
        if (application.Status != TransferStatus.Lodged)
        {
            return Result.Failure(Error.Rule("Hse.TransferNotLodged", $"{application.ApplicationNumber} is {application.Status}; its checklist is closed."));
        }

        if (TransferChecklist.IsLive(requirement))
        {
            return Result.Failure(Error.Rule("Hse.LiveCheck", $"{requirement} is checked by the system and cannot be verified by hand."));
        }

        if (!TransferChecklist.Required(application.TransferType, application.Consideration).Contains(requirement))
        {
            return Result.Failure(Error.Rule("Hse.NotRequired", $"{requirement} is not on the checklist for a {application.TransferType} transfer."));
        }

        if (application.Checks.Any(c => c.Requirement == requirement))
        {
            return Result.Failure(Error.Conflict("Hse.AlreadyVerified", $"{requirement} has already been verified."));
        }

        string reference = documentReference.Trim();
        if (requirement == TransferRequirement.FeesReceipted && !await _db.OpenItems.AnyAsync(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable
            && i.ItemType == OpenItemType.Receipt && i.DocumentNumber == reference && (i.TraderId == application.FromCustomerId || i.TraderId == application.ToCustomerId), ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Rule("Hse.FeesNotReceipted", $"No receipt {reference} from either party is on the sales ledger."));
        }

        application.AddCheck(new TransferCheck(requirement, reference, userId, date));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<TransferChecklistLine>>> ChecklistAsync(long companyId, string applicationNumber, DateOnly date, CancellationToken ct = default)
    {
        Result<TransferApplication> found = await FindAsync(companyId, applicationNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        return Result<IReadOnlyList<TransferChecklistLine>>.Success(await LinesAsync(found.Value, date, ct).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<Result> SubmitAsync(long companyId, string applicationNumber, long userId, DateOnly date, CancellationToken ct = default)
    {
        if (!await AllowedAsync(userId, ChangePermission, OperationFlags.Edit, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Rule("Hse.NotAuthorised", "Only an officer with the ownership-change permission can submit a transfer."));
        }

        Result<TransferApplication> found = await FindAsync(companyId, applicationNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        TransferApplication application = found.Value;
        if (application.Status != TransferStatus.Lodged)
        {
            return Result.Failure(Error.Rule("Hse.TransferNotLodged", $"{application.ApplicationNumber} is {application.Status}."));
        }

        IReadOnlyList<TransferChecklistLine> lines = await LinesAsync(application, date, ct).ConfigureAwait(false);
        List<TransferChecklistLine> outstanding = [.. lines.Where(l => !l.Satisfied)];
        if (outstanding.Count > 0)
        {
            return Result.Failure(Error.Rule("Hse.ChecklistIncomplete", "Outstanding: " + string.Join("; ", outstanding.Select(l => l.Detail is null ? l.Requirement.ToString() : $"{l.Requirement} ({l.Detail})")) + "."));
        }

        Clearance clearance = await MunicipalClearanceAsync(application, date, ct).ConfigureAwait(false);
        application.Submit(userId, clearance.CertificateNumber);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        // When ownership transfers have an approval workflow, it approves the transfer instead of the two approving officers.
        Result<bool> governed = await Workflow.WorkflowGate.StartIfDefinedAsync(_db, _workflow, companyId, WorkflowDocumentTypes.OwnershipTransfer, application.ApplicationNumber,
            application.Consideration, userId, ct).ConfigureAwait(false);
        return governed.IsFailure ? Result.Failure(governed.Error) : Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> ApproveAsync(long companyId, string applicationNumber, long userId, DateOnly date, CancellationToken ct = default)
    {
        if (!await AllowedAsync(userId, ApprovePermission, OperationFlags.Approve, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Rule("Hse.NotAuthorised", "Only an officer with the ownership-approval permission can approve a transfer."));
        }

        Result<TransferApplication> found = await FindAsync(companyId, applicationNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        TransferApplication application = found.Value;
        if (application.Status != TransferStatus.Submitted)
        {
            return Result.Failure(Error.Rule("Hse.TransferNotSubmitted", $"{application.ApplicationNumber} is {application.Status}; only a submitted application can be approved."));
        }

        if (await Workflow.WorkflowGate.InProgressAsync(_db, companyId, WorkflowDocumentTypes.OwnershipTransfer, application.ApplicationNumber, ct).ConfigureAwait(false))
        {
            return Result.Failure(Workflow.WorkflowGate.Governed(WorkflowDocumentTypes.OwnershipTransfer, application.ApplicationNumber));
        }

        // SOD-HSE-05: who verified or submitted it cannot approve it; and two different approvers.
        if (application.SubmittedByUserId == userId || application.Checks.Any(c => c.VerifiedByUserId == userId))
        {
            return Result.Failure(Error.Rule("Hse.ApproverVerified", "An officer who verified or submitted the transfer cannot approve it (SOD-HSE-05)."));
        }

        if (application.Approvals.Any(a => a.UserId == userId))
        {
            return Result.Failure(Error.Conflict("Hse.AlreadyApproved", "The second approval must come from a different officer."));
        }

        application.Approve(new TransferApproval(userId, date));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result> CompleteAsync(long companyId, string applicationNumber, DateOnly date, CancellationToken ct = default)
        => _db.AtomicAsync(() => CompleteCoreAsync(companyId, applicationNumber, date, ct), ct);

    private async Task<Result> CompleteCoreAsync(long companyId, string applicationNumber, DateOnly date, CancellationToken ct)
    {
        Result<TransferApplication> found = await FindAsync(companyId, applicationNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        TransferApplication application = found.Value;
        if (application.Status != TransferStatus.Approved)
        {
            return Result.Failure(Error.Rule("Hse.TransferNotApproved", $"{application.ApplicationNumber} is {application.Status}; it needs two approvals first."));
        }

        HousingStand stand = await _db.HousingStands.AsNoTracking().FirstAsync(s => s.Id == application.StandId, ct).ConfigureAwait(false);
        if (stand.CurrentHolderCustomerId != application.FromCustomerId)
        {
            return Result.Failure(Error.Conflict("Hse.HolderChanged", $"Stand {stand.StandNumber} no longer belongs to the transferor."));
        }

        // The live checks again: nothing may have fallen into arrears, or the certificate lapsed, since submission.
        List<TransferChecklistLine> failing = [.. (await LinesAsync(application, date, ct).ConfigureAwait(false)).Where(l => l.IsLive && !l.Satisfied)];
        if (failing.Count > 0)
        {
            return Result.Failure(Error.Rule("Hse.LiveCheckFailed", string.Join("; ", failing.Select(l => $"{l.Requirement}: {l.Detail}")) + "."));
        }

        Clearance clearance = await MunicipalClearanceAsync(application, date, ct).ConfigureAwait(false);
        string incoming = await _db.Customers.Where(c => c.Id == application.ToCustomerId).Select(c => c.Name).FirstAsync(ct).ConfigureAwait(false);
        Result transferred = await _housing.TransferOwnershipAsync(companyId, stand.Id, application.ToCustomerId, incoming, application.Consideration, date,
            housingAccountClear: true, dualApprovalRecorded: true, ct).ConfigureAwait(false);
        if (transferred.IsFailure)
        {
            return transferred;
        }

        if (clearance.PropertyId is long propertyId && application.TransferType is not (TransferType.ChangeOfName or TransferType.Correction))
        {
            Result municipal = await _ownership.TransferAsync(companyId, propertyId, [new OwnerShare(application.ToCustomerId, 100m, true)], date, clearance.CertificateNumber!, null, ct).ConfigureAwait(false);
            if (municipal.IsFailure)
            {
                return municipal;
            }
        }

        application.Complete(date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> RejectAsync(long companyId, string applicationNumber, string reason, DateOnly date, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("Hse.RejectionReason", "Give the reason for rejecting the transfer."));
        }

        Result<TransferApplication> found = await FindAsync(companyId, applicationNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        if (found.Value.Status is TransferStatus.Completed or TransferStatus.Rejected)
        {
            return Result.Failure(Error.Rule("Hse.TransferClosed", $"{found.Value.ApplicationNumber} is already {found.Value.Status}."));
        }

        found.Value.Reject(date, reason.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<IReadOnlyList<TransferChecklistLine>> LinesAsync(TransferApplication application, DateOnly date, CancellationToken ct)
    {
        List<TransferChecklistLine> lines = [];
        foreach (TransferRequirement requirement in TransferChecklist.Required(application.TransferType, application.Consideration))
        {
            if (requirement == TransferRequirement.MunicipalClearance)
            {
                Clearance clearance = await MunicipalClearanceAsync(application, date, ct).ConfigureAwait(false);
                lines.Add(new TransferChecklistLine(requirement, true, clearance.Satisfied, clearance.Detail));
            }
            else if (requirement == TransferRequirement.HousingAccountClear)
            {
                string? problem = await HousingAccountProblemAsync(application, ct).ConfigureAwait(false);
                lines.Add(new TransferChecklistLine(requirement, true, problem is null, problem ?? "Clear"));
            }
            else
            {
                TransferCheck? check = application.Checks.FirstOrDefault(c => c.Requirement == requirement);
                lines.Add(new TransferChecklistLine(requirement, false, check is not null, check?.DocumentReference));
            }
        }

        return lines;
    }

    // Municipal clearance from the municipal register: a stand that is rated needs a current, unused clearance certificate.
    private async Task<Clearance> MunicipalClearanceAsync(TransferApplication application, DateOnly date, CancellationToken ct)
    {
        string standNumber = await _db.HousingStands.Where(s => s.Id == application.StandId).Select(s => s.StandNumber).FirstAsync(ct).ConfigureAwait(false);
        long? propertyId = await _db.Properties.Where(p => p.CompanyId == application.CompanyId && p.StandNumber == standNumber).Select(p => (long?)p.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (propertyId is null)
        {
            return new Clearance(true, "Not on the municipal register", null, null);
        }

        List<string> used = await _db.Set<PropertyOwner>().Where(o => o.CompanyId == application.CompanyId && o.PropertyId == propertyId).Select(o => o.Reference).ToListAsync(ct).ConfigureAwait(false);
        string? certificate = await _db.Set<ClearanceCertificate>()
            .Where(c => c.CompanyId == application.CompanyId && c.PropertyId == propertyId && c.IssuedOn <= date && c.ValidUntil >= date && !used.Contains(c.CertificateNumber))
            .OrderByDescending(c => c.IssuedOn).Select(c => c.CertificateNumber).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return certificate is null
            ? new Clearance(false, $"No current clearance certificate for stand {standNumber}", null, propertyId)
            : new Clearance(true, $"Certificate {certificate}", certificate, propertyId);
    }

    // The outgoing holder's housing account: nothing owing net of credits, and no instalment sale still running on the stand.
    private async Task<string?> HousingAccountProblemAsync(TransferApplication application, CancellationToken ct)
    {
        decimal owing = await _db.OpenItems.Where(i => i.CompanyId == application.CompanyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == application.FromCustomerId)
            .SumAsync(i => i.Sign == LedgerSign.Debit ? i.OutstandingBase : -i.OutstandingBase, ct).ConfigureAwait(false);
        if (owing > 0m)
        {
            return $"The transferor owes {owing:N2}";
        }

        return await _db.Set<InstalmentAgreement>().AnyAsync(a => a.StandId == application.StandId && a.Status == InstalmentAgreementStatus.Active, ct).ConfigureAwait(false)
            ? "An instalment sale on the stand is still running"
            : null;
    }

    private async Task<bool> AllowedAsync(long userId, string permission, OperationFlags operation, CancellationToken ct)
        => (await _permissions.ResolveAsync(userId, ct).ConfigureAwait(false)).Has(permission, operation);

    private async Task<Result<TransferApplication>> FindAsync(long companyId, string applicationNumber, CancellationToken ct)
    {
        string number = (applicationNumber ?? string.Empty).Trim();
        TransferApplication? application = await _db.Set<TransferApplication>().Include(a => a.Checks).Include(a => a.Approvals)
            .FirstOrDefaultAsync(a => a.CompanyId == companyId && a.ApplicationNumber == number, ct).ConfigureAwait(false);
        if (application is null)
        {
            return Error.NotFound("Hse.TransferNotFound", $"Transfer application {number} not found.");
        }

        return application;
    }

    private sealed record Clearance(bool Satisfied, string Detail, string? CertificateNumber, long? PropertyId);
}

/// <summary>Concludes an ownership transfer's approval workflow: approval approves it for completion, anything else rejects it.</summary>
internal sealed class OwnershipTransferWorkflowHandler : IWorkflowDocumentHandler
{
    private readonly One4AllDbContext _db;

    public OwnershipTransferWorkflowHandler(One4AllDbContext db) => _db = db;

    public string DocumentType => WorkflowDocumentTypes.OwnershipTransfer;

    public async Task<Result> ConcludeAsync(long companyId, string documentReference, Core.Workflow.WorkflowStatus outcome, long decidedByUserId, string? comment, CancellationToken ct = default)
    {
        TransferApplication? application = await _db.Set<TransferApplication>().Include(a => a.Approvals)
            .FirstOrDefaultAsync(a => a.CompanyId == companyId && a.ApplicationNumber == documentReference && a.Status == TransferStatus.Submitted, ct).ConfigureAwait(false);
        if (application is null)
        {
            return Result.Success();
        }

        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        if (outcome == Core.Workflow.WorkflowStatus.Approved)
        {
            application.ApproveThroughWorkflow(new TransferApproval(decidedByUserId, today));
        }
        else
        {
            application.Reject(today, string.IsNullOrWhiteSpace(comment) ? "Turned down in the approval workflow" : comment.Trim());
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}
