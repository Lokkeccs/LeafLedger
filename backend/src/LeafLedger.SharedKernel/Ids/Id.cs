using System.Diagnostics.CodeAnalysis;

namespace LeafLedger.SharedKernel;

/// <summary>
/// Marker for an entity kind, carrying the API-boundary prefix (e.g. "je", "acc").
/// The prefix is a presentation concern only; it never reaches storage.
/// </summary>
public interface IEntityTag
{
    static abstract string Prefix { get; }
}

/// <summary>
/// A strongly-typed identifier backed by a ULID. The canonical storage form is a
/// <see cref="Guid"/> (Postgres <c>uuid</c>, 16 bytes) whose byte order preserves the
/// ULID's lexicographic/temporal ordering. The human-facing form carries a per-kind
/// prefix (<c>je_…</c>) and exists only at the API boundary. See WP P1-WP03 / risk-review N1.
/// </summary>
/// <typeparam name="T">The entity kind, supplying the boundary prefix.</typeparam>
[SuppressMessage(
    "Design",
    "CA1000:Do not declare static members on generic types",
    Justification = "Static factory methods are the idiomatic construction API for this value type.")]
public readonly record struct Id<T> where T : IEntityTag
{
    private readonly Ulid _value;

    private Id(Ulid value) => _value = value;

    /// <summary>Generate a fresh identifier.</summary>
    public static Id<T> New() => new(Ulid.NewUlid());

    public static Id<T> FromUlid(Ulid value) => new(value);

    /// <summary>The underlying ULID.</summary>
    public Ulid Value => _value;

    /// <summary>
    /// Order-preserving storage form. Built big-endian so byte-wise <c>uuid</c> comparison
    /// in Postgres matches ULID order (avoids the .NET Guid little-endian trap).
    /// </summary>
    public Guid ToStorage()
    {
        Span<byte> bytes = stackalloc byte[16];
        _value.TryWriteBytes(bytes);
        return new Guid(bytes, bigEndian: true);
    }

    public static Id<T> FromStorage(Guid uuid)
    {
        Span<byte> bytes = stackalloc byte[16];
        uuid.TryWriteBytes(bytes, bigEndian: true, out _);
        return new Id<T>(new Ulid(bytes));
    }

    /// <summary>The prefixed, human-facing boundary form, e.g. <c>je_01ARZ3…</c>.</summary>
    public string ToBoundaryString() => $"{T.Prefix}_{_value}";

    /// <summary>
    /// Parse a prefixed boundary string. Rejects a missing/wrong prefix and a malformed ULID
    /// as typed failures rather than exceptions.
    /// </summary>
    public static Result<Id<T>> ParseBoundary(string? text)
    {
        var prefix = $"{T.Prefix}_";
        if (string.IsNullOrEmpty(text) || !text.StartsWith(prefix, StringComparison.Ordinal))
        {
            return Result<Id<T>>.Failure(new DomainError(
                "id.invalid_prefix", $"Identifier must start with '{prefix}'."));
        }

        var raw = text[prefix.Length..];
        return Ulid.TryParse(raw, out var ulid)
            ? Result<Id<T>>.Success(new Id<T>(ulid))
            : Result<Id<T>>.Failure(new DomainError("id.invalid_ulid", "Identifier body is not a valid ULID."));
    }

    public override string ToString() => ToBoundaryString();
}
