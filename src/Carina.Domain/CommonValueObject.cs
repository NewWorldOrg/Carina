namespace Carina.Domain;

public abstract class CommonValueObject<TValue> : IEquatable<CommonValueObject<TValue>>
    where TValue : notnull
{
    protected CommonValueObject(TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    public TValue Value { get; }

    public bool Equals(CommonValueObject<TValue>? other)
        => other is not null
           && other.GetType() == GetType()
           && EqualityComparer<TValue>.Default.Equals(Value, other.Value);

    public override bool Equals(object? obj) => Equals(obj as CommonValueObject<TValue>);

    public override int GetHashCode() => HashCode.Combine(GetType(), Value);

    public override string ToString() => Value.ToString() ?? string.Empty;
}
