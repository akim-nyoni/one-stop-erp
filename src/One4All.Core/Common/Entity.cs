namespace One4All.Core.Common;

/// <summary>
/// Base class for domain entities keyed by a <see cref="long"/> identity (the default for
/// transactional tables per the brief). Provides identity equality and a domain-event buffer.
/// </summary>
public abstract class Entity : IEquatable<Entity>
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>Initializes the entity with a given identity (0 for a not-yet-persisted entity).</summary>
    /// <param name="id">The database identity.</param>
    protected Entity(long id) => Id = id;

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    protected Entity()
    {
    }

    /// <summary>The primary identity. Zero until the entity is first persisted.</summary>
    public long Id { get; protected set; }

    /// <summary>
    /// The concurrency token mapped to PostgreSQL's system <c>xmin</c> column, giving every
    /// editable aggregate optimistic concurrency without an explicit version column.
    /// </summary>
    public uint RowVersion { get; protected set; }

    /// <summary>The domain events raised by this entity and not yet dispatched.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>Buffers a domain event to be dispatched after the owning transaction commits.</summary>
    /// <param name="domainEvent">The event to raise.</param>
    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>Clears the buffered domain events. Called by the infrastructure after dispatch.</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <inheritdoc />
    public bool Equals(Entity? other)
    {
        if (other is null || other.GetType() != GetType())
        {
            return false;
        }

        // Transient entities (Id == 0) are only ever equal by reference.
        return Id != 0 && other.Id != 0 && Id == other.Id;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Entity entity && Equals(entity);

    /// <inheritdoc />
    public override int GetHashCode() => Id == 0 ? base.GetHashCode() : HashCode.Combine(GetType(), Id);

    /// <summary>Identity equality operator.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when both refer to the same persisted entity.</returns>
    public static bool operator ==(Entity? left, Entity? right) => Equals(left, right);

    /// <summary>Identity inequality operator.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when the operands are not the same persisted entity.</returns>
    public static bool operator !=(Entity? left, Entity? right) => !Equals(left, right);
}
