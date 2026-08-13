namespace Carina.Domain.DriverStatus;

public sealed class DriverSocketPath : CommonValueObject<string>
{
    public DriverSocketPath(string value)
        : base(Validated(value))
    {
    }

    private static string Validated(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!value.StartsWith('/'))
        {
            throw new ArgumentException($"Driver socket path must be absolute, but was '{value}'.", nameof(value));
        }

        return value;
    }
}
