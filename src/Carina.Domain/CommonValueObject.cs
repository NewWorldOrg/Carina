namespace Carina.Domain;

/// <summary>
/// Base class for single-valued value objects, including identifiers.
/// </summary>
/// <remarks>
/// Two instances are equal when they are of the same concrete type and carry the
/// same value, so that distinct concepts sharing a primitive representation (for
/// example a network id and a service id) never compare equal.
/// </remarks>
/// <typeparam name="TValue">The wrapped primitive type.</typeparam>
public abstract class CommonValueObject<TValue> : IEquatable<CommonValueObject<TValue>>
    where TValue : notnull
{
    /// <summary>Initializes the value object.</summary>
    /// <param name="value">The wrapped value.</param>
    protected CommonValueObject(TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>The wrapped value.</summary>
    public TValue Value { get; }

    /// <inheritdoc />
    public bool Equals(CommonValueObject<TValue>? other)
        => other is not null
           && other.GetType() == GetType()
           && EqualityComparer<TValue>.Default.Equals(Value, other.Value);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as CommonValueObject<TValue>);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(GetType(), Value);

    /// <inheritdoc />
    public override string ToString() => Value.ToString() ?? string.Empty;
}
