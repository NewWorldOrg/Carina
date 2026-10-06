using System.Text;

using Carina.Domain.Programmes;

namespace Carina.Domain.Migration;

/// <summary>
/// The words of a source rule's keyword or of the words it leaves out, with the programme marks the
/// source system wrote as words taken out of them: a mark's symbol, its letters in brackets, or a
/// word that is nothing but the one letter of a mark.
/// </summary>
public sealed record SourceRuleWords
{
    private static readonly IReadOnlyDictionary<char, char> Closing = new Dictionary<char, char>
    {
        ['['] = ']',
        ['［'] = '］',
        ['【'] = '】',
    };

    private static readonly IReadOnlyDictionary<string, ProgrammeMark> Lettered = LetteredMarks();

    private static readonly IReadOnlyDictionary<int, ProgrammeMark> Symbolised =
        ProgrammeMarks.Symbols.ToDictionary(symbol => symbol.CodePoint, symbol => symbol.Mark);

    private SourceRuleWords(IReadOnlyList<string> words, IReadOnlyList<ProgrammeMark> marks)
    {
        Words = words;
        Marks = marks;
    }

    public IReadOnlyList<string> Words { get; }

    public IReadOnlyList<ProgrammeMark> Marks { get; }

    public string Spelt => string.Join(' ', Words);

    public static SourceRuleWords Read(string said)
    {
        ArgumentNullException.ThrowIfNull(said);

        List<string> words = [];
        HashSet<ProgrammeMark> marks = [];

        foreach (string word in said.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Alone(word) is { } mark)
            {
                marks.Add(mark);

                continue;
            }

            words.AddRange(Apart(word, marks));
        }

        return new SourceRuleWords(words, [.. marks.Order()]);
    }

    private static ProgrammeMark? Alone(string word)
    {
        string folded = ProgrammeSearchText.Folded(word);

        return folded.Length == 1 && folded[0] > '\u007F' && Lettered.TryGetValue(folded, out ProgrammeMark mark)
            ? mark
            : null;
    }

    private static IEnumerable<string> Apart(string word, HashSet<ProgrammeMark> marks)
    {
        var piece = new StringBuilder(word.Length);
        int index = 0;

        while (index < word.Length)
        {
            if (Marked(word, index) is not { } found)
            {
                piece.Append(word[index]);
                index++;

                continue;
            }

            marks.Add(found.Mark);
            index += found.Width;

            if (piece.Length > 0)
            {
                yield return piece.ToString();
            }

            piece.Clear();
        }

        if (piece.Length > 0)
        {
            yield return piece.ToString();
        }
    }

    private static (ProgrammeMark Mark, int Width)? Marked(string word, int index)
    {
        if (Rune.TryGetRuneAt(word, index, out Rune rune) && Symbolised.TryGetValue(rune.Value, out ProgrammeMark symbolised))
        {
            return (symbolised, rune.Utf16SequenceLength);
        }

        if (!Closing.TryGetValue(word[index], out char closing))
        {
            return null;
        }

        int closed = word.IndexOf(closing, index + 1);

        return closed > index + 1 && Lettered.TryGetValue(
            ProgrammeSearchText.Folded(word[(index + 1)..closed]),
            out ProgrammeMark lettered)
            ? (lettered, closed - index + 1)
            : null;
    }

    private static Dictionary<string, ProgrammeMark> LetteredMarks()
    {
        Dictionary<string, ProgrammeMark> lettered = new(StringComparer.Ordinal)
        {
            ["鍵"] = ProgrammeMark.ParentalLock,
        };

        foreach (ProgrammeMarkSymbol symbol in ProgrammeMarks.Symbols)
        {
            string letters = ProgrammeSearchText.Folded(symbol.Symbol);

            if (letters != symbol.Symbol)
            {
                lettered[letters] = symbol.Mark;
            }
        }

        return lettered;
    }
}
