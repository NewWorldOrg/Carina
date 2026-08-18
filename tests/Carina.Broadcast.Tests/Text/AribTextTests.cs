using Carina.Broadcast.Text;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.Text;

public sealed class AribTextTests
{
    [Fact]
    public void WithoutAnyEscapeTheLeftHalfIsKanjiAndTheRightHalfIsHiragana()
    {
        byte[] bytes = new AribTextWriter().Kanji("文字").Hiragana("てすと").ToArray();

        Assert.Equal("文字てすと", AribText.Decode(bytes));
    }

    [Fact]
    public void EveryCodeOfTheHiraganaSetIsTheCharacterTheStandardPutsThere()
    {
        for (int index = 0; index < AribTextWriter.HiraganaCodes.Length; index++)
        {
            char expected = AribTextWriter.HiraganaCodes[index];

            Assert.Equal(expected.ToString(), AribText.Decode([(byte)(AribTextWriter.FirstCode + index | 0x80)]));
        }

        for (int index = 0; index < AribTextWriter.HiraganaMarkCodes.Length; index++)
        {
            char expected = AribTextWriter.HiraganaMarkCodes[index];

            Assert.Equal(expected.ToString(), AribText.Decode([(byte)(AribTextWriter.FirstMarkCode + index | 0x80)]));
        }
    }

    [Fact]
    public void EveryCodeOfTheKatakanaSetIsTheCharacterTheStandardPutsThere()
    {
        for (int index = 0; index < AribTextWriter.KatakanaCodes.Length; index++)
        {
            char expected = AribTextWriter.KatakanaCodes[index];
            byte[] bytes = new AribTextWriter().Raw(0x1D, (byte)(AribTextWriter.FirstCode + index)).ToArray();

            Assert.Equal(expected.ToString(), AribText.Decode(bytes));
        }

        for (int index = 0; index < AribTextWriter.KatakanaMarkCodes.Length; index++)
        {
            char expected = AribTextWriter.KatakanaMarkCodes[index];
            byte[] bytes = new AribTextWriter().Raw(0x1D, (byte)(AribTextWriter.FirstMarkCode + index)).ToArray();

            Assert.Equal(expected.ToString(), AribText.Decode(bytes));
        }
    }

    [Fact]
    public void ASingleShiftLastsForOneCharacterOnly()
    {
        byte[] bytes = new AribTextWriter().KatakanaBySingleShift("カ").Kanji("局").ToArray();

        Assert.Equal("カ局", AribText.Decode(bytes));
    }

    [Fact]
    public void ALockingShiftMovesTheKatakanaSetIntoTheRightHalfUntilItIsMovedBack()
    {
        byte[] bytes = new AribTextWriter()
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
        byte[] bytes = new AribTextWriter()
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
        byte[] bytes = new AribTextWriter()
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
        byte[] bytes = new AribTextWriter()
            .DesignateHalfWidthKatakanaToG0()
            .Raw(0x21, 0x36)
            .ToArray();

        Assert.Equal("｡ｶ", AribText.Decode(bytes));
    }

    [Fact]
    public void ACellTheStandardLeavesEmptyBecomesAVisibleSubstitute()
    {
        byte[] bytes = new AribTextWriter().Kanji("局").KanjiCell(4, 90).Kanji("局").ToArray();

        Assert.Equal($"局{AribText.UnknownCharacter}局", AribText.Decode(bytes));
    }

    [Fact]
    public void CustomGlyphsAreDroppedButTheirBytesAreStillConsumed()
    {
        byte[] bytes = new AribTextWriter()
            .DesignateCustomGlyphsToG0()
            .Raw(0x21, 0x22)
            .DesignateKanjiToG0()
            .Kanji("局")
            .ToArray();

        Assert.Equal("局", AribText.Decode(bytes));
    }

    [Fact]
    public void CustomGlyphsDesignatedTwoBytesWideAreDroppedWithoutShiftingWhatFollows()
    {
        byte[] bytes = new AribTextWriter()
            .Kanji("局")
            .DesignateTwoByteCustomGlyphsToG0()
            .Raw(0x21, 0x22)
            .DesignateKanjiToG0()
            .Kanji("局")
            .ToArray();

        Assert.Equal("局局", AribText.Decode(bytes));
    }

    [Fact]
    public void AMosaicCellIsOneByteWideSoOutOfScopeCellsCostOneSubstituteEach()
    {
        byte[] bytes = new AribTextWriter()
            .DesignateMosaicToG0()
            .Raw(0x21, 0x22, 0x23)
            .DesignateKanjiToG0()
            .Kanji("局")
            .ToArray();

        Assert.Equal($"{new string(AribText.UnknownCharacter, 3)}局", AribText.Decode(bytes));
    }

    [Fact]
    public void AnAdditionalSymbolIsTwoBytesWideSoFourBytesCarryTwoOfThem()
    {
        byte[] bytes = new AribTextWriter()
            .DesignateAdditionalSymbolsToG0()
            .Raw(0x7A, 0x50, 0x7A, 0x51)
            .DesignateKanjiToG0()
            .Kanji("局")
            .ToArray();

        Assert.Equal("\U0001F14A\U0001F14C局", AribText.Decode(bytes));
    }

    [Fact]
    public void ATwoByteCellDoesNotEatTheShiftThatFollowsItsFirstHalf()
    {
        byte[] bytes = new AribTextWriter()
            .DesignateKanjiToG0()
            .Raw(0x7A)
            .DesignateAlphanumericToG0()
            .Ascii("4000")
            .ToArray();

        Assert.Equal($"{AribText.UnknownCharacter}4000", AribText.Decode(bytes));
    }

    [Fact]
    public void TheSymbolSetDoesNotReachIntoTheKanjiRowsItDoesNotDefine()
    {
        byte[] bytes = new AribTextWriter()
            .DesignateAdditionalSymbolsToG0()
            .Raw(0x30, 0x21)
            .ToArray();

        Assert.Equal(AribText.UnknownCharacter.ToString(), AribText.Decode(bytes));
    }

    [Fact]
    public void ARowTheStandardLeavesUnusedStillBecomesAVisibleSubstitute()
    {
        byte[] bytes = new AribTextWriter()
            .DesignateAdditionalSymbolsToG0()
            .Raw(0x77, 0x21)
            .ToArray();

        Assert.Equal(AribText.UnknownCharacter.ToString(), AribText.Decode(bytes));
    }

    [Fact]
    public void APositioningControlWithParametersDoesNotSwallowTheTextBehindIt()
    {
        byte[] bytes = new AribTextWriter().Raw(0x1C, 0x40, 0x41).Hiragana("てすと").ToArray();

        Assert.Equal("てすと", AribText.Decode(bytes));
    }

    [Fact]
    public void AColourControlLeavesNoMarkOfItsOwn()
    {
        byte[] bytes = new AribTextWriter().Raw(0x80).Hiragana("てすと").Raw(0x87).ToArray();

        Assert.Equal("てすと", AribText.Decode(bytes));
    }

    [Fact]
    public void AControlSequenceIsConsumedUpToItsTerminator()
    {
        byte[] bytes = new AribTextWriter().Raw(0x9B, 0x30, 0x3B, 0x31, 0x69).Hiragana("てすと").ToArray();

        Assert.Equal("てすと", AribText.Decode(bytes));
    }

    [Fact]
    public void AnEscapeCutOffAtTheEndEndsTheTextInsteadOfReachingPastIt()
    {
        byte[] bytes = new AribTextWriter().Hiragana("てすと").Raw(0x1B, 0x24).ToArray();

        Assert.Equal("てすと", AribText.Decode(bytes));
    }

    [Fact]
    public void ATwoByteCharacterCutInHalfIsDroppedRatherThanGuessed()
    {
        byte[] bytes = new AribTextWriter().Kanji("文字").Raw(0x40).ToArray();

        Assert.Equal("文字", AribText.Decode(bytes));
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
        byte[] bytes = new AribTextWriter().Hiragana("てすと").Raw(0x7F, 0xFF).ToArray();

        Assert.Equal("てすと", AribText.Decode(bytes));
    }

    [Fact]
    public void NoByteRunAtAllMakesTheDecoderThrowOrRunAway()
    {
        var random = new Random(20260814);

        for (int round = 0; round < 2000; round++)
        {
            byte[] bytes = new byte[random.Next(0, 40)];
            random.NextBytes(bytes);

            string decoded = AribText.Decode(bytes);

            Assert.True(decoded.Length <= bytes.Length);
        }
    }
}
