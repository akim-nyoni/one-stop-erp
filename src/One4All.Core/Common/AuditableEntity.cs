namespace One4All.Core.Common;

/// <summary>
/// An <see cref="Entity"/> that records who created and last modified it and when.
/// These fields are populated centrally by the persistence layer's change interceptor,
/// which also writes the fuller <c>audit.change_log</c> record (brief §6). Master data
/// additionally carries a soft-delete flag; transactions are never deleted.
/// </summary>
public abstract class AuditableEntity : Entity
{
    /// <inheritdoc />
    protected AuditableEntity(long id)
        : base(id)
    {
    }

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    protected AuditableEntity()
    {
    }

    /// <summary>UTC instant the row was created.</summary>
    public DateTimeOffset CreatedOnUtc { get; set; }

    /// <summary>User id that created the row.</summary>
    public long CreatedByUserId { get; set; }

    /// <summary>UTC instant the row was last modified, or null if never modified since creation.</summary>
    public DateTimeOffset? ModifiedOnUtc { get; set; }

    /// <summary>User id that last modified the row, or null if never modified.</summary>
    public long? ModifiedByUserId { get; set; }
}

/// <summary>
/// Master-data entity that supports soft delete. Per the brief, only master data may be
/// soft-deleted; transactional records are reversed, never removed.
/// </summary>
public abstract class SoftDeletableEntity : AuditableEntity
{
    /// <inheritdoc />
    protected SoftDeletableEntity(long id)
        : base(id)
    {
    }

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    protected SoftDeletableEntity()
    {
    }

    /// <summary>True when the record has been soft-deleted and should be excluded from normal queries.</summary>
    public bool IsDeleted { get; set; }

    /// <summary>UTC instant the record was soft-deleted, or null if live.</summary>
    public DateTimeOffset? DeletedOnUtc { get; set; }

    /// <summary>User id that soft-deleted the record, or null if live.</summary>
    public long? DeletedByUserId { get; set; }
}
