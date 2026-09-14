namespace One4All.Core.Common;

/// <summary>
/// Classifies the nature of an <see cref="Error"/> so callers (and the API layer)
/// can map a domain failure to an appropriate transport response without string matching.
/// </summary>
public enum ErrorType
{
    /// <summary>A generic failure that does not fit a more specific category.</summary>
    Failure = 0,

    /// <summary>Input failed validation (maps to HTTP 400).</summary>
    Validation = 1,

    /// <summary>A requested resource does not exist (maps to HTTP 404).</summary>
    NotFound = 2,

    /// <summary>The operation conflicts with the current state, e.g. a concurrency or uniqueness clash (maps to HTTP 409).</summary>
    Conflict = 3,

    /// <summary>The caller is authenticated but not permitted to perform the operation (maps to HTTP 403).</summary>
    Forbidden = 4,

    /// <summary>The caller is not authenticated (maps to HTTP 401).</summary>
    Unauthorized = 5,

    /// <summary>A business rule was violated (e.g. posting to a closed period). Distinct from input validation.</summary>
    Rule = 6,
}

/// <summary>
/// An immutable, structured failure value carried by <see cref="Result"/> and <see cref="Result{T}"/>.
/// Errors are data, never exceptions, so they can be returned across layer boundaries and asserted on in tests.
/// </summary>
/// <param name="Code">A stable, machine-readable code, conventionally <c>Area.Reason</c> (e.g. <c>Posting.OutOfBalance</c>).</param>
/// <param name="Message">A human-readable description safe to log; not necessarily safe to show an end user verbatim.</param>
/// <param name="Type">The <see cref="ErrorType"/> category.</param>
public sealed record Error(string Code, string Message, ErrorType Type = ErrorType.Failure)
{
    /// <summary>The canonical "no error" sentinel used by successful results.</summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    /// <summary>Creates a general <see cref="ErrorType.Failure"/> error.</summary>
    /// <param name="code">Stable machine-readable code.</param>
    /// <param name="message">Human-readable description.</param>
    /// <returns>A failure error.</returns>
    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);

    /// <summary>Creates a <see cref="ErrorType.Validation"/> error.</summary>
    /// <param name="code">Stable machine-readable code.</param>
    /// <param name="message">Human-readable description.</param>
    /// <returns>A validation error.</returns>
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    /// <summary>Creates a <see cref="ErrorType.NotFound"/> error.</summary>
    /// <param name="code">Stable machine-readable code.</param>
    /// <param name="message">Human-readable description.</param>
    /// <returns>A not-found error.</returns>
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    /// <summary>Creates a <see cref="ErrorType.Conflict"/> error.</summary>
    /// <param name="code">Stable machine-readable code.</param>
    /// <param name="message">Human-readable description.</param>
    /// <returns>A conflict error.</returns>
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    /// <summary>Creates a <see cref="ErrorType.Forbidden"/> error.</summary>
    /// <param name="code">Stable machine-readable code.</param>
    /// <param name="message">Human-readable description.</param>
    /// <returns>A forbidden error.</returns>
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    /// <summary>Creates a <see cref="ErrorType.Rule"/> (business-rule) error.</summary>
    /// <param name="code">Stable machine-readable code.</param>
    /// <param name="message">Human-readable description.</param>
    /// <returns>A business-rule error.</returns>
    public static Error Rule(string code, string message) => new(code, message, ErrorType.Rule);

    /// <summary>Returns the code and message for logs and diagnostics.</summary>
    /// <returns>A string of the form <c>[Code] Message</c>.</returns>
    public override string ToString() => Code.Length == 0 ? Message : $"[{Code}] {Message}";
}
