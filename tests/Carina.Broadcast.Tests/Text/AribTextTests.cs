using Carina.Broadcast.Tests.Building;
using Carina.Broadcast.Text;

namespace Carina.Broadcast.Tests.Text;

public sealed class AribTextTests
{
    [Fact]
    public void WithoutAnyEscapeTheLeftHalfIsKanjiAndTheRightHalfIsHiragana()
    {
        var bytes = new AribTextWriter().Kanji("総合").Hiragana("てすと").ToArray();

        Assert.Equal("総合てすと", AribText.Decode(bytes));
    }

    [Fact]
    public void EveryCodeOfTheHiraganaSetIsTheCharacterTheStandardPutsThere()
    {
        for (var index = 0; index < AribTextWriter.HiraganaCodes.Length; index++)
        {
            var expected = AribTextWriter.HiraganaCodes[index];

            Assert.Equal(expected.ToString(), AribText.Decode([(byte)(AribTextWriter.FirstCode + index | 0x80)]));
        }

        for (var index = 0; index < AribTextWriter.HiraganaMarkCodes.Length; index++)
        {
            var expected = AribTextWriter.HiraganaMarkCodes[index];

            Assert.Equal(expected.ToString(), AribText.Decode([(byte)(AribTextWriter.FirstMarkCode + index | 0x80)]));
        }
    }

    [Fact]
    public void EveryCodeOfTheKatakanaSetIsTheCharacterTheStandardPutsThere()
    {
        for (var index = 0; index < AribTextWriter.KatakanaCodes.Length; index++)
        {
            var expected = AribTextWriter.KatakanaCodes[index];
            var bytes = new AribTextWriter().Raw(0x1D, (byte)(AribTextWriter.FirstCode + index)).ToArray();

            Assert.Equal(expected.ToString(), AribText.Decode(bytes));
        }

        for (var index = 0; index < AribTextWriter.KatakanaMarkCodes.Length; index++)
        {
            var expected = AribTextWriter.KatakanaMarkCodes[index];
            var bytes = new AribTextWriter().Raw(0x1D, (byte)(AribTextWriter.FirstMarkCode + index)).ToArray();

            Assert.Equal(expected.ToString(), AribText.Decode(bytes));
        }
    }

    [Fact]
    public void ASingleShiftLastsForOneCharacterOnly()
    {
        var bytes = new AribTextWriter().KatakanaBySingleShift("カ").Kanji("局").ToArray();

        Assert.Equal("カ局", AribText.Decode(bytes));
    }

    [Fact]
    public void ALockingShiftMovesTheKatakanaSetIntoTheRightHalfUntilItIsMovedBack()
    {
        var bytes = new AribTextWriter()
            .DesignateKatakanaToG1()
            .LockingShiftOneRight()
            .KatakanaOnTheRight("テスト")
            .Kanji("局")
            .ToArray();

        Assert.Equal("テスト局", AribText.Decode(bytes));
    }

    [Fact]
    public void AlphanumericIsDesignatedByEscapeAndReadsAsPlainLatin()
    {
        var bytes = new AribTextWriter()
            .DesignateAlphanumericToG0()
            .Ascii("Carina 1")
            .DesignateKanjiToG0()
            .Kanji("局")
            .ToArray();

        Assert.Equal("Carina 1局", AribText.Decode(bytes));
    }

    [Fact]
    public void ALockingShiftIntoTheOtherSetOnTheLeftIsUndoneByShiftingBack()
    {
        var bytes = new AribTextWriter()
            .DesignateAlphanumericToG1()
            .LockingShiftOne()
            .Ascii("ABC")
            .LockingShiftZero()
            .Kanji("局")
            .ToArray();

        Assert.Equal("ABC局", AribText.Decode(bytes));
    }

    [Fact]
    public void TheHalfWidthKatakanaSetKeepsItsOwnCodePoints()
    {
        var bytes = new AribTextWriter()
            .DesignateHalfWidthKatakanaToG0()
            .Raw(0x21, 0x36)
            .ToArray();

        Assert.Equal("｡ｶ", AribText.Decode(bytes));
    }

    [Fact]
    public void ACellTheStandardLeavesEmptyBecomesAVisibleSubstitute()
    {
        var bytes = new AribTextWriter().Kanji("局").KanjiCell(4, 90).Kanji("局").ToArray();

        Assert.Equal($"局{AribText.UnknownCharacter}局", AribText.Decode(bytes));
    }

    [Fact]
    public void CustomGlyphsAreDroppedButTheirBytesAreStillConsumed()
    {
        var bytes = new AribTextWriter()
            .DesignateCustomGlyphsToG0()
            .Raw(0x21, 0x22)
            .DesignateKanjiToG0()
            .Kanji("局")
            .ToArray();

        Assert.Equal("局", AribText.Decode(bytes));
    }

    [Fact]
    public void MosaicCellsAreOutOfScopeAndBecomeASubstitute()
    {
        var bytes = new AribTextWriter()
            .DesignateMosaicToG0()
            .Raw(0x21)
            .DesignateKanjiToG0()
            .Kanji("局")
            .ToArray();

        Assert.Equal($"{AribText.UnknownCharacter}局", AribText.Decode(bytes));
    }

    [Fact]
    public void TheAdditionalSymbolSetIsOutOfScopeAndBecomesASubstitute()
    {
        var bytes = new AribTextWriter()
            .DesignateAdditionalSymbolsToG0()
            .Raw(0x7A, 0x50)
            .DesignateKanjiToG0()
            .Kanji("局")
            .ToArray();

        Assert.Equal($"{AribText.UnknownCharacter}局", AribText.Decode(bytes));
    }

    [Fact]
    public void APositioningControlWithParametersDoesNotSwallowTheTextBehindIt()
    {
        var bytes = new AribTextWriter().Raw(0x1C, 0x40, 0x41).Hiragana("てすと").ToArray();

        Assert.Equal("てすと", AribText.Decode(bytes));
    }

    [Fact]
    public void AColourControlLeavesNoMarkOfItsOwn()
    {
        var bytes = new AribTextWriter().Raw(0x80).Hiragana("てすと").Raw(0x87).ToArray();

        Assert.Equal("てすと", AribText.Decode(bytes));
    }

    [Fact]
    public void AControlSequenceIsConsumedUpToItsTerminator()
    {
        var bytes = new AribTextWriter().Raw(0x9B, 0x30, 0x3B, 0x31, 0x69).Hiragana("てすと").ToArray();

        Assert.Equal("てすと", AribText.Decode(bytes));
    }

    [Fact]
    public void AnEscapeCutOffAtTheEndEndsTheTextInsteadOfReachingPastIt()
    {
        var bytes = new AribTextWriter().Hiragana("てすと").Raw(0x1B, 0x24).ToArray();

        Assert.Equal("てすと", AribText.Decode(bytes));
    }

    [Fact]
    public void ATwoByteCharacterCutInHalfIsDroppedRatherThanGuessed()
    {
        var bytes = new AribTextWriter().Kanji("総合").Raw(0x40).ToArray();

        Assert.Equal("総合", AribText.Decode(bytes));
    }

    [Fact]
    public void NoBytesDecodeToNoText()
    {
        Assert.Equal(string.Empty, AribText.Decode(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void ASpaceInEitherHalfIsASpace()
    {
        Assert.Equal("  ", AribText.Decode([0x20, 0xA0]));
    }

    [Fact]
    public void DeleteIsNotACharacter()
    {
        var bytes = new AribTextWriter().Hiragana("てすと").Raw(0x7F, 0xFF).ToArray();

        Assert.Equal("てすと", AribText.Decode(bytes));
    }

    [Fact]
    public void NoByteRunAtAllMakesTheDecoderThrowOrRunAway()
    {
        var random = new Random(20260814);

        for (var round = 0; round < 2000; round++)
        {
            var bytes = new byte[random.Next(0, 40)];
            random.NextBytes(bytes);

            var decoded = AribText.Decode(bytes);

            Assert.True(decoded.Length <= bytes.Length);
        }
    }
}
