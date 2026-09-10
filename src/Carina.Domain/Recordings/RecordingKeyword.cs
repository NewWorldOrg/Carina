using Carina.Domain.Programmes;

namespace Carina.Domain.Recordings;

public sealed record RecordingKeyword
{
    public const int LongestKeyword = 100;

    public const int MostWords = 8;

    private static readonly char[] BetweenWords = [' ', '　'];

    private RecordingKeyword(string asked, IReadOnlyList<string> words)
    {
        Asked = asked;
        Words = words;
    }

    public string Asked { get; }

    public IReadOnlyList<string> Words { get; }

    public static RecordingKeyword? For(string? keyword)
    {
        string asked = (keyword ?? string.Empty).Trim(BetweenWords);

        if (asked.Length > LongestKeyword || WordsIn(asked) is not { } words)
        {
            return null;
        }

        return new RecordingKeyword(asked, words);
    }

    private static IReadOnlyList<string>? WordsIn(string asked)
    {
        string[] apart = asked.Split(BetweenWords, StringSplitOptions.RemoveEmptyEntries);

        if (apart.Length > MostWords)
        {
            return null;
        }

        return
        [
            .. apart
                .Select(word => ProgrammeSearchText.Folded(word).Trim())
                .Where(word => word.Length > 0),
        ];
    }
}
