using Carina.Domain.Base;

namespace Carina.Domain.Encodings;

public sealed class EncodeLabel : CommonValueObject<string>
{
    public const int Longest = 64;

    public EncodeLabel(string value)
        : base(Validated(value))
    {
    }

    private static string Validated(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        string trimmed = value.Trim();

        if (trimmed.Length > Longest)
        {
            throw new ArgumentException(
                $"A label is at most {Longest} characters, but this one has {trimmed.Length}.",
                nameof(value));
        }

        foreach (char letter in trimmed)
        {
            if (char.IsControl(letter))
            {
                throw new ArgumentException("A label is read by a person, so it carries no control character.", nameof(value));
            }
        }

        return trimmed;
    }
}
