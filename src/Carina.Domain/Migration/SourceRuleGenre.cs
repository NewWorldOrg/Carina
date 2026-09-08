namespace Carina.Domain.Migration;

public sealed record SourceRuleGenre
{
    public SourceRuleGenre(int genre, int? subGenre)
    {
        Genre = Counted(genre, nameof(genre));
        SubGenre = subGenre is { } named ? Counted(named, nameof(subGenre)) : null;
    }

    public int Genre { get; }

    public int? SubGenre { get; }

    private static int Counted(int value, string parameterName)
        => value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(parameterName, value, "A genre of the source system is numbered from zero.");
}
