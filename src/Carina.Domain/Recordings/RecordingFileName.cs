using Carina.Domain.Base;

namespace Carina.Domain.Recordings;

public sealed class RecordingFileName : CommonValueObject<string>
{
    public const int MaxLength = 255;

    private static readonly char[] Separators = ['/', '\\', '\0'];

    public RecordingFileName(string value)
        : base(Validated(value))
    {
    }

    public static RecordingFileName For(RecordingId id, string extension)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(extension);

        return new RecordingFileName(id.Wire + extension);
    }

    public bool Names(RecordingId id)
    {
        ArgumentNullException.ThrowIfNull(id);

        return Value.Contains(id.Wire, StringComparison.Ordinal);
    }

    private static string Validated(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length > MaxLength)
        {
            throw new ArgumentException(
                $"A file name is at most {MaxLength} characters, but this one has {value.Length}.",
                nameof(value));
        }

        if (value.IndexOfAny(Separators) >= 0)
        {
            throw new ArgumentException(
                "A file name is a single name, so it carries no separator.",
                nameof(value));
        }

        if (value.Contains("..", StringComparison.Ordinal) || value is ".")
        {
            throw new ArgumentException("A file name names a file, never the way out of its room.", nameof(value));
        }

        if (value.Trim().Length != value.Length)
        {
            throw new ArgumentException("A file name carries no surrounding space.", nameof(value));
        }

        return value;
    }
}
