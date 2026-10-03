using Carina.Broadcast.Text;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.Text;

public sealed class AribCaptionDataTests
{
    private static readonly byte[] Body = CaptionWriter.Positioned(7, 2, new AribTextWriter().Kanji("合成字幕"));

    [Fact]
    public void BrPd019TheBodyOfAStatementInTheFirstLanguageIsWhatIsRead()
    {
        byte[] carried = CaptionWriter.Carried(CaptionWriter.CaptionDataIdentifier, CaptionWriter.Statement(Body));

        Assert.Equal(Body, AribCaptionData.Statement(carried));
    }

    [Fact]
    public void BrPd019AStatementInTheSecondGroupOfTheFirstLanguageIsReadToo()
    {
        byte[] carried = CaptionWriter.Carried(CaptionWriter.CaptionDataIdentifier, CaptionWriter.DataGroup(0x21, StatementData(Units(Unit(0x20, Body)))));

        Assert.Equal(Body, AribCaptionData.Statement(carried));
    }

    [Fact]
    public void BrPd019TheManagementDataTheSecondLanguageAndTheSuperimpositionAreNotStatementsToRead()
    {
        byte[] management = CaptionWriter.Carried(CaptionWriter.CaptionDataIdentifier, CaptionWriter.Management());
        byte[] secondLanguage = CaptionWriter.Carried(
            CaptionWriter.CaptionDataIdentifier,
            CaptionWriter.DataGroup(0x02, StatementData(Units(Unit(0x20, Body)))));
        byte[] superimposed = CaptionWriter.Carried(CaptionWriter.SuperimposeDataIdentifier, CaptionWriter.Statement(Body));

        Assert.Null(AribCaptionData.Statement(management));
        Assert.Null(AribCaptionData.Statement(secondLanguage));
        Assert.Null(AribCaptionData.Statement(superimposed));
    }

    [Fact]
    public void BrPd019AStatementThatNamesWhenItIsShownStillHandsOverItsBody()
    {
        byte[] units = Units(Unit(0x20, Body));
        byte[] data = [0x80, 0x00, 0x00, 0x00, 0x00, 0x00, (byte)(units.Length >> 16), (byte)(units.Length >> 8), (byte)units.Length, .. units];
        byte[] carried = CaptionWriter.Carried(CaptionWriter.CaptionDataIdentifier, CaptionWriter.DataGroup(0x01, data));

        Assert.Equal(Body, AribCaptionData.Statement(carried));
    }

    [Fact]
    public void BrPd019OnlyTheBodyUnitsAreReadAndTheyAreReadInOrder()
    {
        byte[] more = new AribTextWriter().Kanji("字幕").ToArray();
        byte[] carried = CaptionWriter.Carried(
            CaptionWriter.CaptionDataIdentifier,
            CaptionWriter.DataGroup(0x01, StatementData(Units(Unit(0x20, Body), Unit(0x30, [0x01, 0x02, 0x03]), Unit(0x20, more)))));

        Assert.Equal([.. Body, .. more], Assert.IsType<byte[]>(AribCaptionData.Statement(carried)));
    }

    [Fact]
    public void BrPd019DataWhoseLengthsDoNotAddUpIsDropped()
    {
        byte[] whole = CaptionWriter.Carried(CaptionWriter.CaptionDataIdentifier, CaptionWriter.Statement(Body));
        byte[] brokenUnit = CaptionWriter.Carried(
            CaptionWriter.CaptionDataIdentifier,
            CaptionWriter.DataGroup(0x01, StatementData([0x1F, 0x20, 0x00, 0x00, 0x40, 0x0C])));
        byte[] notAUnit = CaptionWriter.Carried(
            CaptionWriter.CaptionDataIdentifier,
            CaptionWriter.DataGroup(0x01, StatementData([0x1E, 0x20, 0x00, 0x00, 0x01, 0x0C])));

        Assert.Null(AribCaptionData.Statement(whole.AsSpan(0, whole.Length - 6)));
        Assert.Null(AribCaptionData.Statement(whole.AsSpan(0, 2)));
        Assert.Null(AribCaptionData.Statement([]));
        Assert.Null(AribCaptionData.Statement(brokenUnit));
        Assert.Null(AribCaptionData.Statement(notAUnit));
    }

    [Fact]
    public void BrPd019AStatementWithNoBodyIsAnEmptyStatementRatherThanNone()
    {
        byte[] carried = CaptionWriter.Carried(
            CaptionWriter.CaptionDataIdentifier,
            CaptionWriter.DataGroup(0x01, StatementData(Units(Unit(0x30, [0x01])))));

        Assert.Equal([], Assert.IsType<byte[]>(AribCaptionData.Statement(carried)));
    }

    private static byte[] StatementData(byte[] units)
        =>
        [
            0x00,
            (byte)(units.Length >> 16),
            (byte)(units.Length >> 8),
            (byte)units.Length,
            .. units,
        ];

    private static byte[] Units(params byte[][] units) => [.. units.SelectMany(unit => unit)];

    private static byte[] Unit(byte parameter, byte[] body)
        =>
        [
            0x1F,
            parameter,
            (byte)(body.Length >> 16),
            (byte)(body.Length >> 8),
            (byte)body.Length,
            .. body,
        ];
}
