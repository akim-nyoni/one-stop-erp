using One4All.Core.Common;

namespace One4All.Core.System;

/// <summary>Where a request carrying an idempotency key has got to.</summary>
public enum IdempotencyState
{
    /// <summary>The first request with the key is still running.</summary>
    InProgress = 1,

    /// <summary>It finished; its response is kept to replay.</summary>
    Completed = 2,
}

/// <summary>
/// One idempotency key a user sent with a posting request (brief §2.4): the request it belongs to (a hash, so the same key cannot be reused
/// for a different request) and, once finished, the response replayed to any retry instead of posting again.
/// </summary>
public sealed class IdempotencyRecord : Entity
{
    private IdempotencyRecord()
    {
        Key = string.Empty;
        Method = string.Empty;
        Path = string.Empty;
        RequestHash = string.Empty;
    }

    /// <summary>Starts a record for a request.</summary>
    /// <param name="userId">Who sent it.</param>
    /// <param name="key">The key they sent.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The path.</param>
    /// <param name="requestHash">A hash of the method, path, query and body.</param>
    /// <param name="startedOnUtc">When it started.</param>
    public IdempotencyRecord(long userId, string key, string method, string path, string requestHash, DateTimeOffset startedOnUtc)
    {
        UserId = userId;
        Key = key;
        Method = method;
        Path = path;
        RequestHash = requestHash;
        StartedOnUtc = startedOnUtc;
        State = IdempotencyState.InProgress;
    }

    /// <summary>Who sent it.</summary>
    public long UserId { get; private set; }

    /// <summary>The key.</summary>
    public string Key { get; private set; }

    /// <summary>The HTTP method.</summary>
    public string Method { get; private set; }

    /// <summary>The path.</summary>
    public string Path { get; private set; }

    /// <summary>A hash of the request.</summary>
    public string RequestHash { get; private set; }

    /// <summary>Running or finished.</summary>
    public IdempotencyState State { get; private set; }

    /// <summary>When the first request started.</summary>
    public DateTimeOffset StartedOnUtc { get; private set; }

    /// <summary>When it finished.</summary>
    public DateTimeOffset? CompletedOnUtc { get; private set; }

    /// <summary>The response status code.</summary>
    public int? ResponseStatus { get; private set; }

    /// <summary>The response content type.</summary>
    public string? ResponseContentType { get; private set; }

    /// <summary>The response body.</summary>
    public byte[]? ResponseBody { get; private set; }

    /// <summary>Keeps the finished response.</summary>
    /// <param name="status">Status code.</param>
    /// <param name="contentType">Content type.</param>
    /// <param name="body">Body.</param>
    /// <param name="completedOnUtc">When.</param>
    public void Complete(int status, string? contentType, byte[] body, DateTimeOffset completedOnUtc)
    {
        State = IdempotencyState.Completed;
        ResponseStatus = status;
        ResponseContentType = contentType;
        ResponseBody = body;
        CompletedOnUtc = completedOnUtc;
    }
}
