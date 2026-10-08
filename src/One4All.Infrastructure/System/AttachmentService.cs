using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.System;
using One4All.Application.Attachments;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using One4All.Licensing.Core;

namespace One4All.Infrastructure.SystemServices;

/// <summary>Files attached to records, checked, checksummed and never deleted (brief §4 attachments).</summary>
public sealed class AttachmentService : IAttachmentService
{
    private readonly One4AllDbContext _db;
    private readonly IClock _clock;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="clock">The clock.</param>
    public AttachmentService(One4AllDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <inheritdoc />
    public Task<Result<long>> AttachAsync(long companyId, AttachmentTarget target, string targetKey, string fileName, byte[] content, long userId, string? description, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            string key = (targetKey ?? string.Empty).Trim().ToUpperInvariant();
            string name = Path.GetFileName((fileName ?? string.Empty).Trim());
            if (key.Length == 0 || name.Length == 0 || !Enum.IsDefined(target))
            {
                return Error.Validation("Att.Request", "An attachment needs the record it belongs to and a file name.");
            }

            (string? contentType, string? problem) = AttachmentRules.Check(name, content);
            if (problem is not null)
            {
                return Error.Validation("Att.File", problem);
            }

            if (!await ExistsAsync(companyId, target, key, ct).ConfigureAwait(false))
            {
                return Error.NotFound("Att.TargetNotFound", $"{target} {key} not found.");
            }

            string sha = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            if (await _db.Set<Attachment>().AnyAsync(a => a.CompanyId == companyId && a.Target == target && a.TargetKey == key && a.Sha256 == sha && a.RemovedOnUtc == null, ct).ConfigureAwait(false))
            {
                return Error.Conflict("Att.Duplicate", $"That file is already attached to {target} {key}.");
            }

            Attachment attachment = new(companyId, target, key, name, contentType!, content.LongLength, sha, userId, _clock.UtcNow, string.IsNullOrWhiteSpace(description) ? null : description.Trim());
            _db.Set<Attachment>().Add(attachment);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            _db.Set<AttachmentContent>().Add(new AttachmentContent(attachment.Id, content));
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<long>.Success(attachment.Id);
        }, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AttachmentInfo>> ListAsync(long companyId, AttachmentTarget target, string targetKey, CancellationToken ct = default)
    {
        string key = (targetKey ?? string.Empty).Trim().ToUpperInvariant();
        return await _db.Set<Attachment>().AsNoTracking().Where(a => a.CompanyId == companyId && a.Target == target && a.TargetKey == key && a.RemovedOnUtc == null)
            .OrderBy(a => a.Id)
            .Select(a => new AttachmentInfo(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.Sha256, a.UploadedByUserId, a.UploadedOnUtc, a.Description))
            .ToListAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<AttachmentFile>> OpenAsync(long companyId, long attachmentId, CancellationToken ct = default)
    {
        Attachment? attachment = await _db.Set<Attachment>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == attachmentId && a.CompanyId == companyId, ct).ConfigureAwait(false);
        byte[]? bytes = attachment is null ? null
            : await _db.Set<AttachmentContent>().AsNoTracking().Where(c => c.AttachmentId == attachmentId).Select(c => c.Bytes).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (attachment is null || bytes is null)
        {
            return Error.NotFound("Att.NotFound", $"Attachment {attachmentId} not found.");
        }

        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != attachment.Sha256)
        {
            return Error.Rule("Att.Tampered", $"Attachment {attachment.FileName} no longer matches its checksum: it has been altered outside One4All.");
        }

        return new AttachmentFile(attachment.FileName, attachment.ContentType, bytes);
    }

    /// <inheritdoc />
    public async Task<Result> RemoveAsync(long companyId, long attachmentId, long userId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("Att.Reason", "Say why the attachment is removed."));
        }

        Attachment? attachment = await _db.Set<Attachment>().FirstOrDefaultAsync(a => a.Id == attachmentId && a.CompanyId == companyId && a.RemovedOnUtc == null, ct).ConfigureAwait(false);
        if (attachment is null)
        {
            return Result.Failure(Error.NotFound("Att.NotFound", $"Attachment {attachmentId} not found."));
        }

        // Evidence behind a posted entry stays with it (a reversal is a new entry; the original keeps its support).
        bool posted = attachment.Target switch
        {
            AttachmentTarget.Journal => await _db.Journals.AnyAsync(j => j.CompanyId == companyId && (j.JournalNumber == attachment.TargetKey || j.DocumentNumber == attachment.TargetKey)
                && (j.Status == JournalStatus.Posted || j.Status == JournalStatus.Reversed), ct).ConfigureAwait(false),
            AttachmentTarget.LedgerDocument => true,
            _ => false,
        };
        if (posted)
        {
            return Result.Failure(Error.Rule("Att.Posted", $"{attachment.FileName} supports a posted {attachment.Target} and cannot be removed."));
        }

        attachment.Remove(userId, _clock.UtcNow, reason.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private Task<bool> ExistsAsync(long companyId, AttachmentTarget target, string key, CancellationToken ct) => target switch
    {
        AttachmentTarget.Journal => _db.Journals.AnyAsync(j => j.CompanyId == companyId && (j.JournalNumber == key || j.DocumentNumber!.ToUpper() == key), ct),
        AttachmentTarget.Customer => _db.Customers.AnyAsync(c => c.CompanyId == companyId && c.Code == key, ct),
        AttachmentTarget.Supplier => _db.Suppliers.AnyAsync(s => s.CompanyId == companyId && s.Code == key, ct),
        AttachmentTarget.LedgerDocument => _db.OpenItems.AnyAsync(o => o.CompanyId == companyId && o.DocumentNumber.ToUpper() == key, ct),
        AttachmentTarget.PurchaseOrder => _db.PurchaseOrders.AnyAsync(o => o.CompanyId == companyId && o.OrderNumber.ToUpper() == key, ct),
        AttachmentTarget.FixedAsset => _db.FixedAssets.AnyAsync(a => a.CompanyId == companyId && a.AssetNumber == key, ct),
        AttachmentTarget.Employee => _db.Employees.AnyAsync(e => e.CompanyId == companyId && e.EmployeeNumber.ToUpper() == key, ct),
        AttachmentTarget.Property => _db.Properties.AnyAsync(p => p.CompanyId == companyId && p.StandNumber.ToUpper() == key, ct),
        AttachmentTarget.Student => _db.Students.AnyAsync(s => s.CompanyId == companyId && s.AdmissionNumber.ToUpper() == key, ct),
        AttachmentTarget.Contract => _db.JobContracts.AnyAsync(c => c.CompanyId == companyId && c.ContractNumber.ToUpper() == key, ct),
        AttachmentTarget.WorkflowDocument => _db.WorkflowInstances.AnyAsync(i => i.CompanyId == companyId && (i.DocumentType + "/" + i.DocumentReference).ToUpper() == key, ct),
        _ => Task.FromResult(false),
    };
}
