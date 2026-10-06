namespace Carina.Domain.Programmes;

/// <summary>
/// A mark a broadcaster writes into a programme's name or summary to say what kind of broadcast it
/// is, one for each of the programme attributes in row 90 of the ARIB STD-B24 additional symbols.
/// </summary>
public enum ProgrammeMark
{
    HighDefinition = 1,

    StandardDefinition = 2,

    Progressive = 3,

    Widescreen = 4,

    MultiView = 5,

    SignLanguage = 6,

    Captioned = 7,

    Interactive = 8,

    DataBroadcast = 9,

    Stereo = 10,

    Bilingual = 11,

    MultipleAudio = 12,

    AudioDescription = 13,

    SurroundStereo = 14,

    BModeStereo = 15,

    News = 16,

    Weather = 17,

    Traffic = 18,

    Film = 19,

    Free = 20,

    Paid = 21,

    ParentalLock = 22,

    FirstPart = 23,

    SecondPart = 24,

    Rerun = 25,

    New = 26,

    Premiere = 27,

    Final = 28,

    Live = 29,

    Shopping = 30,

    VoiceCast = 31,

    Dubbed = 32,

    PayPerView = 33,
}

public sealed record ProgrammeMarkSymbol(ProgrammeMark Mark, int CodePoint)
{
    public string Symbol { get; } = char.ConvertFromUtf32(CodePoint);
}

/// <summary>
/// The marks a programme carries, read from the symbols in its name and summary. The symbols are
/// the code points the ARIB STD-B24 additional symbols 90-48 to 90-63 and 90-66 to 90-82 decode to.
/// </summary>
public static class ProgrammeMarks
{
    public static IReadOnlyList<ProgrammeMarkSymbol> Symbols { get; } =
    [
        new(ProgrammeMark.HighDefinition, 0x1F14A),
        new(ProgrammeMark.StandardDefinition, 0x1F14C),
        new(ProgrammeMark.Progressive, 0x1F13F),
        new(ProgrammeMark.Widescreen, 0x1F146),
        new(ProgrammeMark.MultiView, 0x1F14B),
        new(ProgrammeMark.SignLanguage, 0x1F210),
        new(ProgrammeMark.Captioned, 0x1F211),
        new(ProgrammeMark.Interactive, 0x1F212),
        new(ProgrammeMark.DataBroadcast, 0x1F213),
        new(ProgrammeMark.Stereo, 0x1F142),
        new(ProgrammeMark.Bilingual, 0x1F214),
        new(ProgrammeMark.MultipleAudio, 0x1F215),
        new(ProgrammeMark.AudioDescription, 0x1F216),
        new(ProgrammeMark.SurroundStereo, 0x1F14D),
        new(ProgrammeMark.BModeStereo, 0x1F131),
        new(ProgrammeMark.News, 0x1F13D),
        new(ProgrammeMark.Weather, 0x1F217),
        new(ProgrammeMark.Traffic, 0x1F218),
        new(ProgrammeMark.Film, 0x1F219),
        new(ProgrammeMark.Free, 0x1F21A),
        new(ProgrammeMark.Paid, 0x1F21B),
        new(ProgrammeMark.ParentalLock, 0x26BF),
        new(ProgrammeMark.FirstPart, 0x1F21C),
        new(ProgrammeMark.SecondPart, 0x1F21D),
        new(ProgrammeMark.Rerun, 0x1F21E),
        new(ProgrammeMark.New, 0x1F21F),
        new(ProgrammeMark.Premiere, 0x1F220),
        new(ProgrammeMark.Final, 0x1F221),
        new(ProgrammeMark.Live, 0x1F222),
        new(ProgrammeMark.Shopping, 0x1F223),
        new(ProgrammeMark.VoiceCast, 0x1F224),
        new(ProgrammeMark.Dubbed, 0x1F225),
        new(ProgrammeMark.PayPerView, 0x1F14E),
    ];

    public static IReadOnlyList<ProgrammeMark> In(string name, string summary)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(summary);

        return
        [
            .. Symbols
                .Where(symbol => Carries(name, symbol) || Carries(summary, symbol))
                .Select(symbol => symbol.Mark),
        ];
    }

    private static bool Carries(string text, ProgrammeMarkSymbol symbol)
        => text.Contains(symbol.Symbol, StringComparison.Ordinal);
}
