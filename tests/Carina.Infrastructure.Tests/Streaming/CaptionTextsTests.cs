using Carina.BroadcastTestSupport;
using Carina.Domain.Captions;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class CaptionTextsTests
{
    private const long Lift = 9_000_000_000;

    private const long Second = 90_000;

    [Fact]
    public void BrPd019EachStatementChangesTheTextAtItsOwnMomentOnTheFilesClock()
    {
        CaptionTexts texts = new(Lift);

        texts.Read(Frame(Lift + (8 * Second), Statement(CaptionWriter.Positioned(7, 2, new AribTextWriter().Kanji("合成字幕")))));
        texts.Read(Frame(Lift + (9 * Second), Statement([CaptionWriter.ClearScreen])));

        Assert.Equal([new CaptionLine(8 * Second, "合成字幕"), new CaptionLine(9 * Second, null)], texts.Lines);
    }

    [Fact]
    public void BrPd019AWaitAtTheEndOfAStatementTakesItsTextOffTheScreenThatLongAfterIt()
    {
        CaptionTexts texts = new(Lift);

        texts.Read(Frame(
            Lift + (8 * Second),
            Statement(CaptionWriter.Positioned(7, 2, new AribTextWriter().Kanji("合成字幕").Raw(CaptionWriter.Time, CaptionWriter.WaitFor, 0x40 + 20)))));

        Assert.Equal([new CaptionLine(8 * Second, "合成字幕"), new CaptionLine(10 * Second, null)], texts.Lines);
    }

    [Fact]
    public void BrPd019AStatementThatArrivesBeforeTheLastOneWasDueToLeaveReplacesItThere()
    {
        CaptionTexts texts = new(Lift);

        texts.Read(Frame(
            Lift + (8 * Second),
            Statement(CaptionWriter.Positioned(7, 2, new AribTextWriter().Kanji("合成").Raw(CaptionWriter.Time, CaptionWriter.WaitFor, 0x40 + 20)))));
        texts.Read(Frame(Lift + (9 * Second), Statement(CaptionWriter.Positioned(7, 2, new AribTextWriter().Kanji("字幕")))));

        Assert.Equal([new CaptionLine(8 * Second, "合成"), new CaptionLine(9 * Second, "字幕")], texts.Lines);
    }

    [Fact]
    public void BrPd019TheManagementDataAndTheSameTextAgainChangeNothing()
    {
        CaptionTexts texts = new(Lift);
        byte[] statement = Statement(CaptionWriter.Positioned(7, 2, new AribTextWriter().Kanji("合成字幕")));

        texts.Read(Frame(Lift, CaptionWriter.Carried(CaptionWriter.CaptionDataIdentifier, CaptionWriter.Management())));
        texts.Read(Frame(Lift + Second, statement));
        texts.Read(Frame(Lift + (2 * Second), statement));
        texts.Read(Frame(Lift + (3 * Second), [0x01, 0x02]));

        Assert.Equal([new CaptionLine(Second, "合成字幕")], texts.Lines);
    }

    [Fact]
    public void BrPd019AMomentBeforeTheLiftIsKeptAsTheNegativeMomentTheFileBeganWith()
    {
        CaptionTexts texts = new(Lift);

        texts.Read(Frame(Lift - Second, Statement(CaptionWriter.Positioned(7, 2, new AribTextWriter().Kanji("字")))));

        Assert.Equal([new CaptionLine(-Second, "字")], texts.Lines);
    }

    private static byte[] Statement(byte[] body)
        => CaptionWriter.Carried(CaptionWriter.CaptionDataIdentifier, CaptionWriter.Statement(body));

    private static NutFrame Frame(long pts, byte[] data) => new(LivePts.Of((ulong)pts), data, 1);
}
