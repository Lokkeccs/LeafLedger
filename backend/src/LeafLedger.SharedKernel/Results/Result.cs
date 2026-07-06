using System.Diagnostics.CodeAnalysis;

namespace LeafLedger.SharedKernel;

/// <summary>
/// A domain error: a stable machine code, a human message, and an optional category.
/// Expected failures are represented as <see cref="DomainError"/> inside a <see cref="Result"/>,
/// never as exceptions.
/// </summary>
public sealed record DomainError(string Code, string Message, string? Category = null);

/// <summary>
/// Functional outcome without a value. Success or a <see cref="DomainError"/>.
/// </summary>
public readonly record struct Result
{
    private Result(bool isSuccess, DomainError? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public DomainError? Error { get; }

    public static Result Success() => new(true, null);

    public static Result Failure(DomainError error) =>
        new(false, error ?? throw new ArgumentNullException(nameof(error)));

    public TOut Match<TOut>(Func<TOut> onSuccess, Func<DomainError, TOut> onFailure) =>
        IsSuccess ? onSuccess() : onFailure(Error!);
}

/// <summary>
/// Functional outcome carrying a value on success, or a <see cref="DomainError"/> on failure.
/// The failure path never throws; <see cref="Value"/> throws only on misuse.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1000:Do not declare static members on generic types",
    Justification = "Static factory methods are the idiomatic construction API for this value type.")]
public readonly record struct Result<T>
{
    private readonly T? _value;

    private Result(bool isSuccess, T? value, DomainError? error)
    {
        IsSuccess = isSuccess;
        _value = value;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public DomainError? Error { get; }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access the value of a failed Result.");

    public static Result<T> Success(T value) => new(true, value, null);

    public static Result<T> Failure(DomainError error) =>
        new(false, default, error ?? throw new ArgumentNullException(nameof(error)));

    public Result<TOut> Map<TOut>(Func<T, TOut> map) =>
        IsSuccess ? Result<TOut>.Success(map(_value!)) : Result<TOut>.Failure(Error!);

    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> bind) =>
        IsSuccess ? bind(_value!) : Result<TOut>.Failure(Error!);

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<DomainError, TOut> onFailure) =>
        IsSuccess ? onSuccess(_value!) : onFailure(Error!);
}
