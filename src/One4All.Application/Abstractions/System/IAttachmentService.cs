using One4All.Core.Common;
using One4All.Core.System;

namespace One4All.Application.Abstractions.System;

/// <summary>An attachment as listed.</summary>
/// <param name="Id">Its id.</param>
/// <param name="FileName">The file name.</param>
/// <param name="ContentType">The media type.</param>
/// <param name="SizeBytes">The size.</param>
/// <param name="Sha256">The checksum.</param>
/// <param name="UploadedByUserId">Who uploaded it.</param>
/// <param name="UploadedOnUtc">When.</param>
/// <param name="Description">What it is.</param>
public sealed record AttachmentInfo(long Id, string FileName, string ContentType, long SizeBytes, string Sha256, long UploadedByUserId, DateTimeOffset UploadedOnUtc, string? Description);

/// <summary>An attachment's file, checked against its checksum.</summary>
/// <param name="FileName">The file name.</param>
/// <param name="ContentType">The media type.</param>
/// <param name="Bytes">The bytes.</param>
public sealed record AttachmentFile(string FileName, string ContentType, byte[] Bytes);

/// <summary>
/// Files attached to records (brief §4 attachments): checked by type, content signature and size, stored with a SHA-256 checksum verified on
/// every open, listed by record, never deleted — a removal keeps the file with its reason, and posted journals and ledger documents keep
/// their evidence for good.
/// </summary>
public interface IAttachmentService
{
    /// <summary>Attaches a file to a record (which must exist); the same file twice on one record is refused.</summary>
    Task<Result<long>> AttachAsync(long companyId, AttachmentTarget target, string targetKey, string fileName, byte[] content, long userId, string? description, CancellationToken ct = default);

    /// <summary>The files on a record (removed ones left out).</summary>
    Task<IReadOnlyList<AttachmentInfo>> ListAsync(long companyId, AttachmentTarget target, string targetKey, CancellationToken ct = default);

    /// <summary>Opens a file, refusing it if its bytes no longer match the checksum.</summary>
    Task<Result<AttachmentFile>> OpenAsync(long companyId, long attachmentId, CancellationToken ct = default);

    /// <summary>Removes a file from its record with a reason (refused on posted journals and ledger documents).</summary>
    Task<Result> RemoveAsync(long companyId, long attachmentId, long userId, string reason, CancellationToken ct = default);
}
