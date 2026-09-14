namespace One4All.Core.Common;

/// <summary>
/// Marker for a domain event — something that happened in the domain that other parts of the
/// system react to (e.g. a journal was posted, so sub-ledgers and period balances update).
/// Raised by aggregates and dispatched after the owning transaction commits.
/// </summary>
public interface IDomainEvent
{
    /// <summary>The instant the event occurred, in UTC.</summary>
    DateTimeOffset OccurredOnUtc { get; }
}

/// <summary>
/// Convenience base for domain events that stamps <see cref="OccurredOnUtc"/> at construction.
/// </summary>
public abstract record DomainEvent : IDomainEvent
{
    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; init; } = DateTimeOffset.UtcNow;
}
