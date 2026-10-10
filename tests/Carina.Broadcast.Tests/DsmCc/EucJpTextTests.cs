using System.Text;
using Carina.Broadcast.DsmCc;
using Carina.Broadcast.Text;

namespace Carina.Broadcast.Tests.DsmCc;

public sealed class EucJpTextTests
{
    [Fact]
    public void BR_BD_002_AsciiAndLineBreaksPassThrough()
    {
        Assert.Equal("<bml>\r\n\t</bml>", EucJpText.Decode("<bml>\r\n\t</bml>"u8));
    }

    [Fact]
    public void BR_BD_002_TwoBytesAboveA0AreTheRowAndCellOfJisX0208()
    {
        Assert.Equal("天気予報", EucJpText.Decode([0xC5, 0xB7, 0xB5, 0xA4, 0xCD, 0xBD, 0xCA, 0xF3]));
    }

    [Fact]
    public void BR_BD_002_SingleShiftTwoCarriesAHalfWidthKatakana()
    {
        Assert.Equal("ｱﾟ", EucJpText.Decode([0x8E, 0xB1, 0x8E, 0xDF]));
    }

    [Theory]
    [InlineData(0xFA, 0xA1, "⛌")]
    [InlineData(0xFA, 0xBE, "⛠")]
    [InlineData(0xFC, 0xA1, "➡")]
    [InlineData(0xFE, 0xA1, "Ⅰ")]
    [InlineData(0xF5, 0xA1, "㐂")]
    public void BR_BD_002_RowsEightyFiveToNinetyFourAreTheAribAdditionalSymbolsAndKanji(int lead, int trail, string expected)
    {
        Assert.Equal($"a{expected}b", EucJpText.Decode([0x61, (byte)lead, (byte)trail, 0x62]));
    }

    [Fact]
    public void BR_BD_002_AnAribAdditionalSymbolLeavesAsTheSameCharacterTheCaptionsUse()
    {
        Assert.True(AribSymbols.TryMap(90, 1, out string symbol));

        Assert.Equal(symbol, EucJpText.Decode([0xFA, 0xA1]));
    }

    [Fact]
    public void BR_BD_002_ACellNoTableAssignsIsTheUnknownCharacter()
    {
        Assert.Equal($"{AribText.UnknownCharacter}", EucJpText.Decode([0xA9, 0xA1]));
    }

    [Fact]
    public void BR_BD_002_SingleShiftThreeForJisX0212IsOneUnknownCharacterAndTheTextGoesOn()
    {
        Assert.Equal($"{AribText.UnknownCharacter}a", EucJpText.Decode([0x8F, 0xB0, 0xA1, 0x61]));
    }

    [Fact]
    public void BR_BD_002_ALeadByteWithoutItsTrailIsUnknownAndTheNextByteIsReadOnItsOwn()
    {
        Assert.Equal($"{AribText.UnknownCharacter}A", EucJpText.Decode([0xC5, 0x41]));
        Assert.Equal($"a{AribText.UnknownCharacter}", EucJpText.Decode([0x61, 0xC5]));
        Assert.Equal($"{AribText.UnknownCharacter}", EucJpText.Decode([0x8E]));
    }

    [Theory]
    [InlineData(0x80)]
    [InlineData(0xA0)]
    [InlineData(0xFF)]
    public void BR_BD_002_AByteNoCodeSetStartsWithIsUnknown(int code)
    {
        Assert.Equal($"{AribText.UnknownCharacter}", EucJpText.Decode([(byte)code]));
    }

    [Fact]
    public void BR_BD_002_TheTextLeavesAsUtf8()
    {
        Assert.Equal(Encoding.UTF8.GetBytes("天⛌ｱ"), EucJpText.ToUtf8([0xC5, 0xB7, 0xFA, 0xA1, 0x8E, 0xB1]));
    }

    [Fact]
    public void BR_BV_001_NoRunOfRandomBytesMakesTheDecoderThrow()
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
