namespace One4All.Core.Audit;

/// <summary>
/// A row change caught by a database trigger on a sensitive table (<c>audit.db_change</c>, brief §6 "second net"): written by the database
/// itself, so changes made outside the application (direct SQL) are recorded too, with the database user, application name and client
/// address. Append-only; secrets (password and PIN hashes, authenticator secrets) are left out.
/// </summary>
public sealed class DatabaseChange
{
    private DatabaseChange()
    {
        TableName = string.Empty;
        Operation = string.Empty;
        DbUser = string.Empty;
    }

    /// <summary>Gets the id.</summary>
    public long Id { get; private set; }

    /// <summary>Gets when it happened.</summary>
    public DateTimeOffset OccurredOnUtc { get; private set; }

    /// <summary>Gets the schema-qualified table.</summary>
    public string TableName { get; private set; }

    /// <summary>Gets INSERT, UPDATE or DELETE.</summary>
    public string Operation { get; private set; }

    /// <summary>Gets the changed row's id.</summary>
    public string? RowId { get; private set; }

    /// <summary>Gets the row before (JSON).</summary>
    public string? OldValues { get; private set; }

    /// <summary>Gets the row after (JSON).</summary>
    public string? NewValues { get; private set; }

    /// <summary>Gets the database user.</summary>
    public string DbUser { get; private set; }

    /// <summary>Gets the connection's application name (One4All for the application).</summary>
    public string? ApplicationName { get; private set; }

    /// <summary>Gets the client address.</summary>
    public string? ClientAddress { get; private set; }
}
