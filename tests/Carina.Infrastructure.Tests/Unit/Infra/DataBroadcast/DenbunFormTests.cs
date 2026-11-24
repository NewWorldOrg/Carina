using System.Text;

using Carina.Broadcast.DsmCc;
using Carina.Infrastructure.DataBroadcast;

namespace Carina.Infrastructure.Tests.Unit.Infra.DataBroadcast;

[Trait("Category", "Unit")]
public sealed class DenbunFormTests
{
    [Fact(DisplayName = "BR-BD-011: letters, digits, hyphen, underscore and full stop follow Denbun= as they are")]
    public void LettersDigitsAndTheThreeMarksFollowAsTheyAre()
    {
        byte[] form = DenbunForm.Of(Encoding.ASCII.GetBytes("AZaz09-_."));

        Assert.Equal(
            new byte[] { 0x44, 0x65, 0x6E, 0x62, 0x75, 0x6E, 0x3D, 0x41, 0x5A, 0x61, 0x7A, 0x30, 0x39, 0x2D, 0x5F, 0x2E },
            form);
    }

    [Fact(DisplayName = "BR-BD-011: an equals sign and an ampersand are written as %XX and a space as a plus sign")]
    public void TheMarksOfAFormArePercentEncodedAndASpaceIsAPlusSign()
    {
        byte[] form = DenbunForm.Of(Encoding.ASCII.GetBytes("a=1&b=2 c"));

        Assert.Equal("Denbun=a%3D1%26b%3D2+c", Encoding.ASCII.GetString(form));
    }

    [Fact(DisplayName = "BR-BD-011: a full-width character in EUC-JP is written byte by byte as %XX in upper case")]
    public void AFullWidthCharacterInEucJpIsWrittenByteByByte()
    {
        byte[] text = EucJpText.Encode("a=1&b=あ");

        byte[] form = DenbunForm.Of(text);

        Assert.Equal(new byte[] { 0x61, 0x3D, 0x31, 0x26, 0x62, 0x3D, 0xA4, 0xA2 }, text);
        Assert.Equal("Denbun=a%3D1%26b%3D%A4%A2", Encoding.ASCII.GetString(form));
    }

    [Fact(DisplayName = "BR-BD-011: every other byte is written as %XX in upper case, the marks a URL leaves alone included")]
    public void EveryOtherByteIsPercentEncodedInUpperCase()
    {
        byte[] form = DenbunForm.Of([0x00, 0x09, 0x0A, 0x25, 0x2A, 0x2B, 0x2F, 0x7E, 0x7F, 0xAB, 0xFF]);

        Assert.Equal("Denbun=%00%09%0A%25%2A%2B%2F%7E%7F%AB%FF", Encoding.ASCII.GetString(form));
    }

    [Fact(DisplayName = "BR-BD-011: an empty text leaves Denbun= alone")]
    public void AnEmptyTextLeavesTheNameAlone()
    {
        byte[] form = DenbunForm.Of([]);

        Assert.Equal(new byte[] { 0x44, 0x65, 0x6E, 0x62, 0x75, 0x6E, 0x3D }, form);
    }
}
