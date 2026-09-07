using Carina.Domain.Base;

namespace Carina.Domain.Migration;

public sealed class MigrationSourceName : CommonValueObject<string>
{
    public const int MaxLength = 200;

    public MigrationSourceName(string value)
        : base(Validated(value))
    {
    }

    private static string Validated(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        string kept = MigrationNote.Of(value);

        if (kept.Length is 0)
        {
            throw new ArgumentException("A source says which system it is.", nameof(value));
        }

        if (kept.Length > MaxLength)
        {
            throw new ArgumentException(
                $"A source name is at most {MaxLength} characters, but this one has {kept.Length}.",
                nameof(value));
        }

        return kept;
    }
}
