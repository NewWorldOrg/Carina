using System.Text;

using Carina.Broadcast.DsmCc;
using Carina.Broadcast.Text;

namespace Carina.Broadcast.Tests.DsmCc;

public sealed class EucJpTextTests
{
    [Fact(DisplayName = "BR-BD-002: ascii and line breaks pass through")]
    public void AsciiAndLineBreaksPassThrough()
    {
        Assert.Equal("<bml>\r\n\t</bml>", EucJpText.Decode("<bml>\r\n\t</bml>"u8));
    }

    [Fact(DisplayName = "BR-BD-002: two bytes above 0xA0 are the row and cell of JIS X 0208")]
    public void TwoBytesAboveA0AreTheRowAndCellOfJisX0208()
    {
        Assert.Equal("天気予報", EucJpText.Decode([0xC5, 0xB7, 0xB5, 0xA4, 0xCD, 0xBD, 0xCA, 0xF3]));
    }

    [Fact(DisplayName = "BR-BD-002: single shift two carries a half width katakana")]
    public void SingleShiftTwoCarriesAHalfWidthKatakana()
    {
        Assert.Equal("ｱﾟ", EucJpText.Decode([0x8E, 0xB1, 0x8E, 0xDF]));
    }

    [Theory(DisplayName = "BR-BD-002: rows 85 to 94 are the ARIB additional symbols and kanji")]
    [InlineData(0xFA, 0xA1, "⛌")]
    [InlineData(0xFA, 0xBE, "⛠")]
    [InlineData(0xFC, 0xA1, "➡")]
    [InlineData(0xFE, 0xA1, "Ⅰ")]
    [InlineData(0xF5, 0xA1, "㐂")]
    public void RowsEightyFiveToNinetyFourAreTheAribAdditionalSymbolsAndKanji(int lead, int trail, string expected)
    {
        Assert.Equal($"a{expected}b", EucJpText.Decode([0x61, (byte)lead, (byte)trail, 0x62]));
    }

    [Fact(DisplayName = "BR-BD-002: an ARIB additional symbol leaves as the same character the captions use")]
    public void AnAribAdditionalSymbolLeavesAsTheSameCharacterTheCaptionsUse()
    {
        Assert.True(AribSymbols.TryMap(90, 1, out string symbol));

        Assert.Equal(symbol, EucJpText.Decode([0xFA, 0xA1]));
    }

    [Fact(DisplayName = "BR-BD-002: a cell no table assigns is the unknown character")]
    public void ACellNoTableAssignsIsTheUnknownCharacter()
    {
        Assert.Equal($"{AribText.UnknownCharacter}", EucJpText.Decode([0xA9, 0xA1]));
    }

    [Fact(DisplayName = "BR-BD-002: single shift three for JIS X 0212 is one unknown character and the text goes on")]
    public void SingleShiftThreeForJisX0212IsOneUnknownCharacterAndTheTextGoesOn()
    {
        Assert.Equal($"{AribText.UnknownCharacter}a", EucJpText.Decode([0x8F, 0xB0, 0xA1, 0x61]));
    }

    [Fact(DisplayName = "BR-BD-002: a lead byte without its trail is unknown and the next byte is read on its own")]
    public void ALeadByteWithoutItsTrailIsUnknownAndTheNextByteIsReadOnItsOwn()
    {
        Assert.Equal($"{AribText.UnknownCharacter}A", EucJpText.Decode([0xC5, 0x41]));
        Assert.Equal($"a{AribText.UnknownCharacter}", EucJpText.Decode([0x61, 0xC5]));
        Assert.Equal($"{AribText.UnknownCharacter}", EucJpText.Decode([0x8E]));
    }

    [Theory(DisplayName = "BR-BD-002: a byte no code set starts with is unknown")]
    [InlineData(0x80)]
    [InlineData(0xA0)]
    [InlineData(0xFF)]
    public void AByteNoCodeSetStartsWithIsUnknown(int code)
    {
        Assert.Equal($"{AribText.UnknownCharacter}", EucJpText.Decode([(byte)code]));
    }

    [Fact(DisplayName = "BR-BD-002: the text leaves as UTF-8")]
    public void TheTextLeavesAsUtf8()
    {
        Assert.Equal(Encoding.UTF8.GetBytes("天⛌ｱ"), EucJpText.ToUtf8([0xC5, 0xB7, 0xFA, 0xA1, 0x8E, 0xB1]));
    }

    [Fact(DisplayName = "BR-BV-001: no run of random bytes makes the decoder throw")]
    public void NoRunOfRandomBytesMakesTheDecoderThrow()
    {
        var random = new Random(20261012);

        for (int round = 0; round < 2000; round++)
        {
            byte[] bytes = new byte[random.Next(0, 64)];
            random.NextBytes(bytes);

            _ = EucJpText.ToUtf8(bytes);
        }
    }
}
