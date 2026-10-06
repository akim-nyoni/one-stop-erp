using One4All.Core.Common;

namespace One4All.Core.System;

/// <summary>Where a backup stands.</summary>
public enum BackupStatus
{
    /// <summary>Taken; not yet checked.</summary>
    Taken = 1,

    /// <summary>Checked: the file matches its hash and the archive reads back.</summary>
    Verified = 2,

    /// <summary>The backup did not complete.</summary>
    Failed = 3,

    /// <summary>The file is missing, altered or unreadable.</summary>
    Corrupt = 4,

    /// <summary>Removed under the retention policy.</summary>
    Pruned = 5,
}

/// <summary>A database backup (<c>sys.backup</c>, brief §9): the dump file, its SHA-256, when, why and how it checked out.</summary>
public sealed class BackupRecord : Entity
{
    private BackupRecord()
    {
        FileName = string.Empty;
        Reason = string.Empty;
    }

    /// <summary>Starts the record.</summary>
    /// <param name="fileName">The dump file's full path.</param>
    /// <param name="reason">Why it was taken (scheduled, before an upgrade…).</param>
    /// <param name="startedOnUtc">When it started.</param>
    public BackupRecord(string fileName, string reason, DateTimeOffset startedOnUtc)
    {
        FileName = fileName;
        Reason = reason;
        StartedOnUtc = startedOnUtc;
    }

    /// <summary>Gets the dump file's path.</summary>
    public string FileName { get; private set; }

    /// <summary>Gets why it was taken.</summary>
    public string Reason { get; private set; }

    /// <summary>Gets when it started.</summary>
    public DateTimeOffset StartedOnUtc { get; private set; }

    /// <summary>Gets when it finished.</summary>
    public DateTimeOffset? CompletedOnUtc { get; private set; }

    /// <summary>Gets the file size.</summary>
    public long SizeBytes { get; private set; }

    /// <summary>Gets the file's SHA-256 (hex).</summary>
    public string? Sha256 { get; private set; }

    /// <summary>Gets the status.</summary>
    public BackupStatus Status { get; private set; }

    /// <summary>Gets the latest message (error, check result).</summary>
    public string? Message { get; private set; }

    /// <summary>Gets where the off-site copy went, if one was made.</summary>
    public string? OffsiteCopy { get; private set; }

    /// <summary>Records a finished backup.</summary>
    /// <param name="at">When.</param>
    /// <param name="size">The size.</param>
    /// <param name="sha256">The hash.</param>
    /// <param name="offsiteCopy">The off-site copy, if any.</param>
    public void Complete(DateTimeOffset at, long size, string sha256, string? offsiteCopy)
    {
        CompletedOnUtc = at;
        SizeBytes = size;
        Sha256 = sha256;
        OffsiteCopy = offsiteCopy;
        Status = BackupStatus.Taken;
    }

    /// <summary>Records a failure or a check result.</summary>
    /// <param name="status">The status.</param>
    /// <param name="message">Why.</param>
    public void Mark(BackupStatus status, string? message)
    {
        Status = status;
        Message = message;
    }
}
