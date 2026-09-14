using System.Diagnostics.CodeAnalysis;

namespace One4All.Core.Common;

/// <summary>
/// The outcome of an operation that can succeed or fail without throwing.
/// Used pervasively so failures (out-of-balance journals, closed periods, permission denials)
/// are explicit return values rather than exceptions.
/// </summary>
public class Result
{
    /// <summary>Initializes a new <see cref="Result"/>. Use the factory methods rather than constructing directly.</summary>
    /// <param name="isSuccess">Whether the operation succeeded.</param>
    /// <param name="error">The failure detail, or <see cref="Error.None"/> on success.</param>
    /// <exception cref="ArgumentException">Thrown when success/error state is inconsistent (guards against silent bugs).</exception>
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
        {
            throw new ArgumentException("A successful result cannot carry an error.", nameof(error));
        }

        if (!isSuccess && error == Error.None)
        {
            throw new ArgumentException("A failed result must carry an error.", nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>True when the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>True when the operation failed. The inverse of <see cref="IsSuccess"/>.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>The failure detail. Equals <see cref="Error.None"/> when <see cref="IsSuccess"/> is true.</summary>
    public Error Error { get; }

    /// <summary>Creates a successful result.</summary>
    /// <returns>A success.</returns>
    public static Result Success() => new(true, Error.None);

    /// <summary>Creates a failed result.</summary>
    /// <param name="error">The failure detail.</param>
    /// <returns>A failure.</returns>
    public static Result Failure(Error error) => new(false, error);

    /// <summary>Creates a successful result carrying a value.</summary>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="value">The value produced by the operation.</param>
    /// <returns>A success carrying <paramref name="value"/>.</returns>
    public static Result<TValue> Success<TValue>(TValue value) => Result<TValue>.Success(value);

    /// <summary>Creates a failed result of a value-bearing type.</summary>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="error">The failure detail.</param>
    /// <returns>A failure of <see cref="Result{TValue}"/>.</returns>
    public static Result<TValue> Failure<TValue>(Error error) => Result<TValue>.Failure(error);
}

/// <summary>
/// The outcome of an operation that yields a value of type <typeparamref name="TValue"/> on success.
/// </summary>
/// <typeparam name="TValue">The type of the produced value.</typeparam>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    private Result(TValue value)
        : base(true, Error.None) => _value = value;

    private Result(Error error)
        : base(false, error) => _value = default;

    /// <summary>
    /// The produced value. Only valid when <see cref="Result.IsSuccess"/> is true.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if accessed on a failed result — a programming error the caller must fix.</exception>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("The value of a failed result cannot be accessed.");

    /// <summary>Creates a successful value result.</summary>
    /// <param name="value">The produced value.</param>
    /// <returns>A success carrying <paramref name="value"/>.</returns>
    public static Result<TValue> Success(TValue value) => new(value);

    /// <summary>Creates a failed value result.</summary>
    /// <param name="error">The failure detail.</param>
    /// <returns>A failure.</returns>
    public static new Result<TValue> Failure(Error error) => new(error);

    /// <summary>Implicitly wraps a value in a successful result for terser call sites.</summary>
    /// <param name="value">The value to wrap.</param>
    public static implicit operator Result<TValue>(TValue value) => Success(value);

    /// <summary>Implicitly wraps an error in a failed result for terser call sites.</summary>
    /// <param name="error">The error to wrap.</param>
    public static implicit operator Result<TValue>(Error error) => Failure(error);

    /// <summary>
    /// Returns <paramref name="value"/> set to the success value and true, or default and false on failure —
    /// enabling <c>if (result.TryGetValue(out var v))</c> call sites.
    /// </summary>
    /// <param name="value">Receives the value on success.</param>
    /// <returns>True when successful.</returns>
    public bool TryGetValue([NotNullWhen(true)] out TValue? value)
    {
        value = IsSuccess ? _value : default;
        return IsSuccess;
    }
}
