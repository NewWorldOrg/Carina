namespace Carina.Broadcast.Text;

internal static class AribGraphicSets
{
    public const int FirstCode = 0x21;

    public const int LastCode = 0x7E;

    private const int Codes = LastCode - FirstCode + 1;

    private static readonly (int Row, int Cell)[] SharedMarks =
    [
        (1, 28),
        (1, 3),
        (1, 54),
        (1, 55),
        (1, 2),
        (1, 6),
    ];

    public static readonly string Hiragana = Kana(kanaRow: 4, kanaCount: 83, repeatMarks: [(1, 21), (1, 22)]);

    public static readonly string Katakana = Kana(kanaRow: 5, kanaCount: 86, repeatMarks: [(1, 19), (1, 20)]);

    public static int Width(GraphicSet set)
        => set is GraphicSet.Kanji or GraphicSet.AdditionalSymbol or GraphicSet.TwoByteDrcs
            or GraphicSet.UnknownTwoByte
            ? 2
            : 1;

    public static bool IsDrcs(GraphicSet set)
        => set is GraphicSet.OneByteDrcs or GraphicSet.TwoByteDrcs;

    public static GraphicSet OneByteSet(int finalByte)
        => finalByte switch
        {
            0x4A or 0x36 => GraphicSet.Alphanumeric,
            0x30 or 0x37 => GraphicSet.Hiragana,
            0x31 or 0x38 => GraphicSet.Katakana,
            0x49 => GraphicSet.HalfWidthKatakana,
            0x32 or 0x33 or 0x34 or 0x35 => GraphicSet.Mosaic,
            0x70 => GraphicSet.Macro,
            _ => GraphicSet.UnknownOneByte,
        };

    public static GraphicSet TwoByteSet(int finalByte)
        => finalByte switch
        {
            0x42 or 0x39 => GraphicSet.Kanji,
            0x3B => GraphicSet.AdditionalSymbol,
            _ => GraphicSet.UnknownTwoByte,
        };

    private static string Kana(int kanaRow, int kanaCount, (int Row, int Cell)[] repeatMarks)
    {
        var codes = new char[Codes];

        for (var cell = 1; cell <= kanaCount; cell++)
        {
            codes[cell - 1] = JisX0208.TryMap(kanaRow, cell, out var kana) ? kana : '\0';
        }

        var marks = repeatMarks.Concat(SharedMarks).ToArray();

        for (var index = 0; index < marks.Length; index++)
        {
            var (row, cell) = marks[index];
            codes[Codes - marks.Length + index] =
                JisX0208.TryMap(row, cell, out var mark) ? mark : '\0';
        }

        return new string(codes);
    }
}
