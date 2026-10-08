using One4All.Core.Common;

namespace One4All.Core.System;

/// <summary>The kinds of record a file can be attached to.</summary>
public enum AttachmentTarget
{
    /// <summary>A general-ledger journal (by journal or document number).</summary>
    Journal = 1,

    /// <summary>A customer.</summary>
    Customer = 2,

    /// <summary>A supplier.</summary>
    Supplier = 3,

    /// <summary>A customer or supplier document on the ledger (invoice, credit note, payment) by document number.</summary>
    LedgerDocument = 4,

    /// <summary>A purchase order.</summary>
    PurchaseOrder = 5,

    /// <summary>A fixed asset.</summary>
    FixedAsset = 6,

    /// <summary>An employee.</summary>
    Employee = 7,

    /// <summary>A municipal property (stand).</summary>
    Property = 8,

    /// <summary>A student.</summary>
    Student = 9,

    /// <summary>A job / contract.</summary>
    Contract = 10,

    /// <summary>A document in an approval workflow, keyed <c>{DocumentType}/{Reference}</c> — shown to its approvers.</summary>
    WorkflowDocument = 11,
}

/// <summary>
/// A file attached to a record (<c>sys.attachment</c>, brief §4 attachments): a scan, contract or delivery note, with its SHA-256 checksum,
/// size and type and who uploaded it. Its bytes are kept apart (<see cref="AttachmentContent"/>) so lists stay light. Attachments are
/// never deleted: a removal is a soft delete with its reason, and evidence on posted documents cannot be removed at all.
/// </summary>
public sealed class Attachment : AuditableEntity, ICompanyScoped
{
    private Attachment()
    {
        TargetKey = string.Empty;
        FileName = string.Empty;
        ContentType = string.Empty;
        Sha256 = string.Empty;
    }

    /// <summary>Records the attachment.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="target">The kind of record.</param>
    /// <param name="targetKey">The record's number or code.</param>
    /// <param name="fileName">The file name.</param>
    /// <param name="contentType">The media type.</param>
    /// <param name="sizeBytes">The size.</param>
    /// <param name="sha256">The SHA-256 checksum (hex).</param>
    /// <param name="uploadedByUserId">Who uploaded it.</param>
    /// <param name="uploadedOnUtc">When.</param>
    /// <param name="description">What it is.</param>
    public Attachment(long companyId, AttachmentTarget target, string targetKey, string fileName, string contentType, long sizeBytes, string sha256, long uploadedByUserId,
        DateTimeOffset uploadedOnUtc, string? description)
    {
        CompanyId = companyId;
        Target = target;
        TargetKey = targetKey;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        UploadedByUserId = uploadedByUserId;
        UploadedOnUtc = uploadedOnUtc;
        Description = description;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The kind of record.</summary>
    public AttachmentTarget Target { get; private set; }

    /// <summary>The record's number or code.</summary>
    public string TargetKey { get; private set; }

    /// <summary>The file name.</summary>
    public string FileName { get; private set; }

    /// <summary>The media type.</summary>
    public string ContentType { get; private set; }

    /// <summary>The size in bytes.</summary>
    public long SizeBytes { get; private set; }

    /// <summary>The SHA-256 checksum (lower-case hex), checked whenever the file is opened.</summary>
    public string Sha256 { get; private set; }

    /// <summary>Who uploaded it.</summary>
    public long UploadedByUserId { get; private set; }

    /// <summary>When.</summary>
    public DateTimeOffset UploadedOnUtc { get; private set; }

    /// <summary>What it is.</summary>
    public string? Description { get; private set; }

    /// <summary>When it was removed, if it was.</summary>
    public DateTimeOffset? RemovedOnUtc { get; private set; }

    /// <summary>Who removed it.</summary>
    public long? RemovedByUserId { get; private set; }

    /// <summary>Why.</summary>
    public string? RemovalReason { get; private set; }

    /// <summary>Removes it from the record (the file is kept).</summary>
    /// <param name="userId">Who.</param>
    /// <param name="when">When.</param>
    /// <param name="reason">Why.</param>
    public void Remove(long userId, DateTimeOffset when, string reason)
    {
        RemovedByUserId = userId;
        RemovedOnUtc = when;
        RemovalReason = reason;
    }
}

/// <summary>An attachment's bytes (<c>sys.attachment_content</c>).</summary>
public sealed class AttachmentContent
{
    private AttachmentContent() => Bytes = [];

    /// <summary>Stores the bytes.</summary>
    /// <param name="attachmentId">The attachment.</param>
    /// <param name="bytes">The bytes.</param>
    public AttachmentContent(long attachmentId, byte[] bytes)
    {
        AttachmentId = attachmentId;
        Bytes = bytes;
    }

    /// <summary>The attachment.</summary>
    public long AttachmentId { get; private set; }

    /// <summary>The bytes.</summary>
    public byte[] Bytes { get; private set; }
}
