namespace One4All.Licensing.Core;

/// <summary>Abstracts the current time so licence validation is testable and deterministic.</summary>
public interface IClock
{
    /// <summary>The current UTC time.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>The system clock.</summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// Stores a monotonic high-water-mark timestamp used to detect clock rollback (brief §11.5). Real
/// implementations persist it encrypted in more than one place (DB and registry/file); if the system
/// clock is ever earlier than the stored mark beyond a tolerance, tampering is assumed.
/// </summary>
public interface ILicenseHighWaterMarkStore
{
    /// <summary>Reads the stored high-water mark, or null if none has been recorded yet.</summary>
    /// <returns>The stored mark, or null.</returns>
    DateTimeOffset? Read();

    /// <summary>Advances the stored high-water mark to <paramref name="value"/> (never moves it backwards).</summary>
    /// <param name="value">The candidate new mark.</param>
    void Advance(DateTimeOffset value);
}
