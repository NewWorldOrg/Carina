using Carina.Domain.Base;

namespace Carina.Domain.Rules;

public sealed class RuleQuery : CommonValueObject<string>
{
    public const int MaxLength = 2048;

    public RuleQuery(string value)
        : base(Validated(value))
    {
    }

    private static string Validated(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length > MaxLength)
        {
            throw new ArgumentException(
                $"A rule query is at most {MaxLength} characters, but this one has {value.Length}.",
                nameof(value));
        }

        if (value.StartsWith('?') || value.Contains('#', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A rule query is the query string a programme search carries, without its leading question mark or any fragment.",
                nameof(value));
        }

        if (value.Split('&').Any(pair => pair.Length is 0 || pair.StartsWith('=')))
        {
            throw new ArgumentException(
                "A rule query is a sequence of named parameters.",
                nameof(value));
        }

        return value;
    }
}
