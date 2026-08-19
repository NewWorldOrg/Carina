using Carina.Domain.Base;

namespace Carina.Domain.Auth;

public sealed class ClientSecret : CommonValueObject<string>
{
    public ClientSecret(string value)
        : base(Validated(value))
    {
    }

    public override string ToString() => "(client secret)";

    private static string Validated(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "A client secret is what the identity provider issued, so it cannot be blank.",
                nameof(value));
        }

        return value;
    }
}
