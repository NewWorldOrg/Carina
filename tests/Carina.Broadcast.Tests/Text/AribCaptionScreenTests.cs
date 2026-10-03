using Carina.Broadcast.Text;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.Text;

public sealed class AribCaptionScreenTests
{
    private const byte ClearScreen = 0x0C;

    private const byte SmallSize = 0x88;

    private const byte NormalSize = 0x8A;

    [Fact]
    public void BrPd019AStatementThatClearsThenWritesShowsItsTextAtOnce()
    {
        AribCaptionScreen screen = new();

        IReadOnlyList<AribCaptionChange> changes = screen.Write(Positioned(7, new AribTextWriter().Kanji("合成字幕")));

        Assert.Equal([new AribCaptionChange(0, "合成字幕")], changes);
    }

    [Fact]
    public void BrPd019AStatementThatOnlyClearsTakesTheTextOffTheScreen()
    {
        AribCaptionScreen screen = new();
        screen.Write(Positioned(7, new AribTextWriter().Kanji("合成字幕")));

        IReadOnlyList<AribCaptionChange> changes = screen.Write([ClearScreen]);

        Assert.Equal([new AribCaptionChange(0, null)], changes);
    }

    [Fact]
    public void BrPd019ClearingAScreenThatShowsNothingChangesNothing()
    {
        Assert.Empty(new AribCaptionScreen().Write([ClearScreen]));
    }

    [Fact]
    public void BrPd019AWaitAtTheEndOfAStatementIsHowLongItsTextStaysOnTheScreen()
    {
        AribCaptionScreen screen = new();

        IReadOnlyList<AribCaptionChange> changes = screen.Write(
            Positioned(7, new AribTextWriter().Kanji("合成字幕").Raw(0x9D, 0x20, 0x40 + 20)));

        Assert.Equal([new AribCaptionChange(0, "合成字幕"), new AribCaptionChange(20, null)], changes);
        Assert.Empty(screen.Write([ClearScreen]));
    }

    [Fact]
    public void BrPd019AWaitPartWayThroughAStatementShowsWhatCameBeforeItAndThenAddsWhatFollows()
    {
        AribCaptionScreen screen = new();

        IReadOnlyList<AribCaptionChange> changes = screen.Write(
        [
            .. Positioned(7, new AribTextWriter().Kanji("合成").Raw(0x9D, 0x20, 0x40 + 10)),
            0x1C,
            0x40 + 8,
            0x40 + 2,
            .. new AribTextWriter().Kanji("字幕").ToArray(),
        ]);

        Assert.Equal([new AribCaptionChange(0, "合成"), new AribCaptionChange(10, "合成\n字幕")], changes);
    }

    [Fact]
    public void BrPd019AStatementThatDoesNotClearAddsToWhatIsAlreadyOnTheScreen()
    {
        AribCaptionScreen screen = new();
        screen.Write(Positioned(7, new AribTextWriter().Kanji("合成")));

        IReadOnlyList<AribCaptionChange> changes = screen.Write(
            [0x1C, 0x40 + 8, 0x40 + 2, .. new AribTextWriter().Kanji("字幕").ToArray()]);

        Assert.Equal([new AribCaptionChange(0, "合成\n字幕")], changes);
    }

    [Fact]
    public void BrPd019TheSameTextWrittenAgainIsNotAChange()
    {
        AribCaptionScreen screen = new();
        screen.Write(Positioned(7, new AribTextWriter().Kanji("合成字幕")));

        Assert.Empty(screen.Write(Positioned(7, new AribTextWriter().Kanji("合成字幕"))));
    }

    [Fact]
    public void BrPd019AMoveToAnotherRowStartsANewLineAndAMoveAlongTheSameRowDoesNot()
    {
        AribCaptionScreen screen = new();

        IReadOnlyList<AribCaptionChange> changes = screen.Write(
        [
            ClearScreen,
            0x1C, 0x40 + 7, 0x40 + 2,
            .. new AribTextWriter().Kanji("合成").ToArray(),
            0x1C, 0x40 + 7, 0x40 + 9,
            .. new AribTextWriter().Kanji("字幕").ToArray(),
            0x1C, 0x40 + 8, 0x40 + 2,
            .. new AribTextWriter().Kanji("局").ToArray(),
            0x0D,
            .. new AribTextWriter().Kanji("字").ToArray(),
        ]);

        Assert.Equal([new AribCaptionChange(0, "合成字幕\n局\n字")], changes);
    }

    [Fact]
    public void BrPd019SpaceAroundALineAndEmptyLinesAreDropped()
    {
        AribCaptionScreen screen = new();

        IReadOnlyList<AribCaptionChange> changes = screen.Write(
        [
            ClearScreen,
            0x1C, 0x40 + 6, 0x40 + 2,
            0x20, 0x20,
            0x1C, 0x40 + 7, 0x40 + 2,
            0x20,
            .. new AribTextWriter().Kanji("合成").ToArray(),
            0x20,
        ]);

        Assert.Equal([new AribCaptionChange(0, "合成")], changes);
    }

    [Fact]
    public void BrPd019AGlyphTheBroadcastSendsAsAPictureBecomesTheGetaMark()
    {
        AribCaptionScreen screen = new();

        IReadOnlyList<AribCaptionChange> changes = screen.Write(
            Positioned(7, new AribTextWriter().DesignateCustomGlyphsToG0().Raw(0x41).DesignateKanjiToG0().Kanji("合成")));

        Assert.Equal([new AribCaptionChange(0, $"{AribCaptionScreen.Unreplaceable}合成")], changes);
    }

    [Fact]
    public void BrPd019AnAdditionalSymbolBecomesItsCharacterAndOneWithNoCharacterBecomesTheGetaMark()
    {
        AribCaptionScreen screen = new();

        IReadOnlyList<AribCaptionChange> changes = screen.Write(
            Positioned(7, new AribTextWriter().Raw(0x7A, 0x50).Raw(0x7C, 0x58)));

        Assert.Equal([new AribCaptionChange(0, $"\U0001F14A{AribCaptionScreen.Unreplaceable}")], changes);
    }

    [Fact]
    public void BrPd019TextWrittenSmallIsRubyAndIsDropped()
    {
        AribCaptionScreen screen = new();

        IReadOnlyList<AribCaptionChange> changes = screen.Write(
        [
            ClearScreen,
            0x1C, 0x40 + 6, 0x40 + 3,
            SmallSize,
            .. new AribTextWriter().Hiragana("てすと").ToArray(),
            NormalSize,
            0x1C, 0x40 + 7, 0x40 + 2,
            .. new AribTextWriter().Kanji("合成").ToArray(),
            0x8B, 0x60,
            .. new AribTextWriter().Hiragana("てすと").ToArray(),
            0x8B, 0x41,
            .. new AribTextWriter().Kanji("字幕").ToArray(),
        ]);

        Assert.Equal([new AribCaptionChange(0, "合成字幕")], changes);
    }

    [Fact]
    public void BrPd019AlphanumericsHiraganaAndKatakanaAreRead()
    {
        AribCaptionScreen screen = new();

        IReadOnlyList<AribCaptionChange> changes = screen.Write(
            Positioned(
                7,
                new AribTextWriter()
                    .Hiragana("てすと")
                    .DesignateKatakanaToG1()
                    .LockingShiftOneRight()
                    .KatakanaOnTheRight("テスト")
                    .DesignateAlphanumericToG0()
                    .Ascii("CARINA")));

        Assert.Equal([new AribCaptionChange(0, "てすとテストCARINA")], changes);
    }

    [Fact]
    public void BrPd019ADefaultMacroDesignatesTheSetsItStandsFor()
    {
        AribCaptionScreen screen = new();

        IReadOnlyList<AribCaptionChange> changes = screen.Write(
        [
            ClearScreen,
            0x1C, 0x40 + 7, 0x40 + 2,
            0x1D, 0x61,
            .. new AribTextWriter().Hiragana("てすと").LockingShiftOneRight().KatakanaOnTheRight("テスト").ToArray(),
            0x1D, 0x60,
            .. new AribTextWriter().LockingShiftOneRight().Raw(0xC1).ToArray(),
            0x1D, 0x62,
            .. new AribTextWriter().LockingShiftOneRight().Raw(0xC1).ToArray(),
            0x1D, 0x6F,
            .. new AribTextWriter().Kanji("字").ToArray(),
        ]);

        Assert.Equal([new AribCaptionChange(0, $"てすとテストA{AribCaptionScreen.Unreplaceable}字")], changes);
    }

    [Fact]
    public void BrPd019ColoursAndOtherControlsLeaveTheTextAsItIs()
    {
        AribCaptionScreen screen = new();

        IReadOnlyList<AribCaptionChange> changes = screen.Write(
        [
            ClearScreen,
            0x9B, 0x37, 0x3B, 0x32, 0x20, 0x56,
            0x1C, 0x40 + 7, 0x40 + 2,
            0x87,
            0x90, 0x20, 0x48,
            0x89,
            .. new AribTextWriter().Kanji("合成").ToArray(),
            0x9D, 0x28, 0x40,
            0x98, 0x41,
            .. new AribTextWriter().Kanji("字幕").ToArray(),
        ]);

        Assert.Equal([new AribCaptionChange(0, "合成字幕")], changes);
    }

    [Fact]
    public void BrPd019APositionGivenInDotsStartsANewLine()
    {
        AribCaptionScreen screen = new();

        IReadOnlyList<AribCaptionChange> changes = screen.Write(
        [
            ClearScreen,
            .. new AribTextWriter().Kanji("合成").ToArray(),
            0x9B, 0x31, 0x30, 0x30, 0x3B, 0x34, 0x30, 0x30, 0x20, 0x61,
            .. new AribTextWriter().Kanji("字幕").ToArray(),
        ]);

        Assert.Equal([new AribCaptionChange(0, "合成\n字幕")], changes);
    }

    [Fact]
    public void BrPd019AStatementCutShortInTheMiddleOfACharacterKeepsWhatCameBefore()
    {
        AribCaptionScreen screen = new();
        byte[] whole = Positioned(7, new AribTextWriter().Kanji("合成"));

        IReadOnlyList<AribCaptionChange> changes = screen.Write(whole.AsSpan(0, whole.Length - 1));

        Assert.Equal([new AribCaptionChange(0, "合")], changes);
    }

    private static byte[] Positioned(int row, AribTextWriter text) => CaptionWriter.Positioned(row, 2, text);
}
