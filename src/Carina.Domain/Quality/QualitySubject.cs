namespace Carina.Domain.Quality;

public enum QualitySubjectKind
{
    Tuner = 1,

    Channel = 2,

    Recording = 3,

    TransportStream = 4,
}

public sealed record QualitySubject
{
    public const int KeyMaxLength = 64;

    private QualitySubject(QualitySubjectKind kind, string key)
    {
        Kind = kind;
        Key = key;
    }

    public QualitySubjectKind Kind { get; }

    public string Key { get; }

    public static QualitySubject Of(QualitySubjectKind kind, string key)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "A subject is one of the four things this domain watches.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (key.Length > KeyMaxLength)
        {
            throw new ArgumentException(
                $"A subject is named in at most {KeyMaxLength} characters, but this one takes {key.Length}.",
                nameof(key));
        }

        return new QualitySubject(kind, key);
    }
}
