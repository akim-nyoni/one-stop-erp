namespace One4All.Application.Abstractions.Integration;

/// <summary>What to do with a request carrying an idempotency key.</summary>
public enum IdempotencyOutcome
{
    /// <summary>First time: run it, then complete or abandon the claim.</summary>
    Proceed = 1,

    /// <summary>Already done: replay the kept response.</summary>
    Replay = 2,

    /// <summary>The first request with the key is still running.</summary>
    InProgress = 3,

    /// <summary>The key was already used for a different request.</summary>
    Mismatch = 4,
}

/// <summary>The store's answer for a key.</summary>
/// <param name="Outcome">What to do.</param>
/// <param name="ClaimId">The claim to complete or abandon (proceed only).</param>
/// <param name="Status">The kept status code (replay only).</param>
/// <param name="ContentType">The kept content type (replay only).</param>
/// <param name="Body">The kept body (replay only).</param>
public sealed record IdempotencyClaim(IdempotencyOutcome Outcome, long? ClaimId = null, int Status = 0, string? ContentType = null, byte[]? Body = null);

/// <summary>
/// Idempotency keys on posting endpoints (brief §2.4): the first request with a key runs and its response is kept; a retry with the same
/// key and request gets that response back without posting again. Keys belong to the user who sent them and are kept for a day.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>How long a key is kept.</summary>
    static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    /// <summary>Claims a key for a request, or says it is a retry, still running, or reused for something else.</summary>
    /// <param name="userId">Who sent it.</param>
    /// <param name="key">The key.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The path.</param>
    /// <param name="requestHash">A hash of the request.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The claim.</returns>
    Task<IdempotencyClaim> BeginAsync(long userId, string key, string method, string path, string requestHash, CancellationToken ct = default);

    /// <summary>Keeps the response of a claimed request.</summary>
    /// <param name="claimId">The claim.</param>
    /// <param name="status">Status code.</param>
    /// <param name="contentType">Content type.</param>
    /// <param name="body">Body.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task.</returns>
    Task CompleteAsync(long claimId, int status, string? contentType, byte[] body, CancellationToken ct = default);

    /// <summary>Lets the key go (the request failed on the server's side, so a retry should run it again).</summary>
    /// <param name="claimId">The claim.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task.</returns>
    Task AbandonAsync(long claimId, CancellationToken ct = default);

    /// <summary>Removes keys past their retention.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>How many were removed.</returns>
    Task<int> PurgeAsync(CancellationToken ct = default);
}
