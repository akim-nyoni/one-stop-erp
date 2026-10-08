using One4All.Core.Common;

namespace One4All.Core.Audit;

/// <summary>
/// A year of the audit log moved out of the live table (brief §5.1): detached from <c>audit.change_log</c> into the archive schema, where it
/// stays queryable, on the licence holder's signed authorisation. It keeps where each company's hash chain had reached, so the chain still
/// verifies across the gap and new entries carry on from it.
/// </summary>
public sealed class AuditArchive : Entity
{
    private AuditArchive()
    {
        TableName = string.Empty;
        ChainHeadsJson = "{}";
        ContentHash = string.Empty;
        AuthorisationKeyId = string.Empty;
    }

    /// <summary>Records an archived year.</summary>
    /// <param name="year">The calendar year.</param>
    /// <param name="tableName">Where its rows now are.</param>
    /// <param name="rowCount">How many rows.</param>
    /// <param name="chainHeadsJson">Each company's last row hash up to and including the year (JSON object, "system" for the system chain).</param>
    /// <param name="contentHash">The SHA-256 of the year's row hashes in order.</param>
    /// <param name="authorisationKeyId">The licence-holder key that authorised it.</param>
    /// <param name="archivedOnUtc">When.</param>
    /// <param name="archivedByUserId">Who ran it.</param>
    public AuditArchive(int year, string tableName, long rowCount, string chainHeadsJson, string contentHash, string authorisationKeyId, DateTimeOffset archivedOnUtc, long? archivedByUserId)
    {
        Year = year;
        TableName = tableName;
        RowCount = rowCount;
        ChainHeadsJson = chainHeadsJson;
        ContentHash = contentHash;
        AuthorisationKeyId = authorisationKeyId;
        ArchivedOnUtc = archivedOnUtc;
        ArchivedByUserId = archivedByUserId;
    }

    /// <summary>Gets the calendar year archived.</summary>
    public int Year { get; private set; }

    /// <summary>Gets the table now holding the year's rows.</summary>
    public string TableName { get; private set; }

    /// <summary>Gets how many rows were archived.</summary>
    public long RowCount { get; private set; }

    /// <summary>Gets each company's last row hash up to and including the year, as JSON.</summary>
    public string ChainHeadsJson { get; private set; }

    /// <summary>Gets the SHA-256 over the year's row hashes in order.</summary>
    public string ContentHash { get; private set; }

    /// <summary>Gets the id of the licence-holder key that authorised the archive.</summary>
    public string AuthorisationKeyId { get; private set; }

    /// <summary>Gets when the year was archived.</summary>
    public DateTimeOffset ArchivedOnUtc { get; private set; }

    /// <summary>Gets who ran the archive.</summary>
    public long? ArchivedByUserId { get; private set; }
}
