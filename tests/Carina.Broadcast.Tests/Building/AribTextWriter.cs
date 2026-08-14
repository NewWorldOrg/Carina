using Carina.Broadcast.Text;

namespace Carina.Broadcast.Tests.Building;

public sealed class AribTextWriter
{
    public const string HiraganaCodes =
        "ぁあぃいぅうぇえぉおかがきぎくぐけげこごさざしじすずせぜそぞただちぢっつづてでとどなにぬねのはばぱひびぴふぶぷへべぺほぼぽまみむめもゃやゅゆょよらりるれろゎわゐゑをん";

    public const string HiraganaMarkCodes = "ゝゞー。「」、・";

    public const string KatakanaCodes =
        "ァアィイゥウェエォオカガキギクグケゲコゴサザシジスズセゼソゾタダチヂッツヅテデトドナニヌネノハバパヒビピフブプヘベペホボポマミムメモャヤュユョヨラリルレロヮワヰヱヲンヴヵヶ";

    public const string KatakanaMarkCodes = "ヽヾー。「」、・";

    public const int FirstCode = 0x21;

    public const int FirstMarkCode = 0x77;

    private static readonly Dictionary<char, (int Row, int Cell)> KanjiCells = BuildKanjiCells();

    private readonly List<byte> bytes = [];

    public AribTextWriter Raw(params byte[] raw)
    {
        bytes.AddRange(raw);

        return this;
    }

    public AribTextWriter Kanji(string text)
    {
        foreach (var character in text)
        {
            var (row, cell) = KanjiCells[character];
            bytes.Add((byte)(0x20 + row));
            bytes.Add((byte)(0x20 + cell));
        }

        return this;
    }

    public AribTextWriter KanjiCell(int row, int cell) => Raw((byte)(0x20 + row), (byte)(0x20 + cell));

    public AribTextWriter Hiragana(string text) => Right(text, HiraganaCodes, HiraganaMarkCodes);

    public AribTextWriter KatakanaBySingleShift(string text)
    {
        foreach (var character in text)
        {
            Raw(0x1D, (byte)CodeOf(character, KatakanaCodes, KatakanaMarkCodes));
        }

        return this;
    }

    public AribTextWriter KatakanaOnTheRight(string text) => Right(text, KatakanaCodes, KatakanaMarkCodes);

    public AribTextWriter Ascii(string text)
    {
        foreach (var character in text)
        {
            bytes.Add((byte)character);
        }

        return this;
    }

    public AribTextWriter DesignateKanjiToG0() => Raw(0x1B, 0x24, 0x42);

    public AribTextWriter DesignateAlphanumericToG0() => Raw(0x1B, 0x28, 0x4A);

    public AribTextWriter DesignateAlphanumericToG1() => Raw(0x1B, 0x29, 0x4A);

    public AribTextWriter DesignateHalfWidthKatakanaToG0() => Raw(0x1B, 0x28, 0x49);

    public AribTextWriter DesignateMosaicToG0() => Raw(0x1B, 0x28, 0x32);

    public AribTextWriter DesignateCustomGlyphsToG0() => Raw(0x1B, 0x28, 0x20, 0x41);

    public AribTextWriter DesignateAdditionalSymbolsToG0() => Raw(0x1B, 0x24, 0x3B);

    public AribTextWriter DesignateKatakanaToG1() => Raw(0x1B, 0x29, 0x31);

    public AribTextWriter LockingShiftZero() => Raw(0x0F);

    public AribTextWriter LockingShiftOne() => Raw(0x0E);

    public AribTextWriter LockingShiftOneRight() => Raw(0x1B, 0x7E);

    public byte[] ToArray() => bytes.ToArray();

    public static int CodeOf(char character, string set, string marks)
    {
        var atSet = set.IndexOf(character, StringComparison.Ordinal);

        if (atSet >= 0)
        {
            return FirstCode + atSet;
        }

        var atMarks = marks.IndexOf(character, StringComparison.Ordinal);

        if (atMarks >= 0)
        {
            return FirstMarkCode + atMarks;
        }

        throw new ArgumentOutOfRangeException(nameof(character), character, "That character is not in this set.");
    }

    private AribTextWriter Right(string text, string set, string marks)
    {
        foreach (var character in text)
        {
            bytes.Add((byte)(CodeOf(character, set, marks) | 0x80));
        }

        return this;
    }

    private static Dictionary<char, (int Row, int Cell)> BuildKanjiCells()
    {
        var cells = new Dictionary<char, (int Row, int Cell)>();

        for (var row = JisX0208.FirstRow; row <= JisX0208.LastRow; row++)
        {
            for (var cell = 1; cell <= JisX0208.CellsPerRow; cell++)
            {
                if (JisX0208.TryMap(row, cell, out var character))
                {
                    cells.TryAdd(character, (row, cell));
                }
            }
        }

        return cells;
    }
}
