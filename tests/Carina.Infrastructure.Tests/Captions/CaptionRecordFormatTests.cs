using Carina.Domain.Captions;
using Carina.Infrastructure.Captions;

namespace Carina.Infrastructure.Tests.Captions;

public sealed class CaptionRecordFormatTests
{
    private static readonly CaptionPlacement Lower = new(120, 900, 640, 72, new byte[] { 0x89, 0x50, 0x4e, 0x47, 1, 2, 3 });

    private static readonly CaptionPlacement Upper = new(0, 0, 1, 1, new byte[] { 0x89, 0x50, 0x4e, 0x47, 9 });

    [Fact]
    public void BrPd016ARecordComesBackAsItWasWrittenWithTheCanvasTheStartAndEveryChange()
    {
        CaptionRecord written = new(
            1440,
            1080,
            TimeSpan.FromSeconds(6115.509),
            [new CaptionCue(550_395_810, Lower), new CaptionCue(550_485_810, null), new CaptionCue(550_575_810, Upper)]);

        CaptionRecord read = Assert.IsType<CaptionRecord>(CaptionRecordFormat.Read(CaptionRecordFormat.Written(written)));

        Assert.Equal((1440, 1080), (read.Width, read.Height));
        Assert.Equal(written.StartsAt, read.StartsAt);
        Assert.Equal(written.Cues.Select(cue => cue.Pts), read.Cues.Select(cue => cue.Pts));
        Assert.Equal([false, true, false], read.Cues.Select(cue => cue.Clears));
        AssertSame(Lower, read.Cues[0].Picture!);
        AssertSame(Upper, read.Cues[2].Picture!);
        Assert.Equal(2, read.Pictures);
    }

    [Fact]
    public void BrPd017MomentsBeyondThirtyThreeBitsAndBeforeZeroComeBackAsTheyWere()
    {
        long beyond = (1L << 33) + 1_234_567;
        CaptionRecord written = new(
            1920,
            1080,
            TimeSpan.FromSeconds(-3.621333),
            [new CaptionCue(-279_000, Lower), new CaptionCue(36_000, null), new CaptionCue(beyond, Upper)]);

        CaptionRecord read = Assert.IsType<CaptionRecord>(CaptionRecordFormat.Read(CaptionRecordFormat.Written(written)));

        Assert.Equal([-279_000L, 36_000L, beyond], read.Cues.Select(cue => cue.Pts));
        Assert.Equal(TimeSpan.FromSeconds(-3.621333), read.StartsAt);
        Assert.Equal(TimeSpan.FromSeconds(-3.1), read.Cues[0].At);
    }

    [Fact]
    public void ARecordWithNoChangesIsStillARecord()
    {
        CaptionRecord read = Assert.IsType<CaptionRecord>(
            CaptionRecordFormat.Read(CaptionRecordFormat.Written(new CaptionRecord(720, 480, TimeSpan.Zero, []))));

        Assert.Empty(read.Cues);
        Assert.Equal(CaptionRecordFormat.HeaderLength, CaptionRecordFormat.Written(read).Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(CaptionRecordFormat.HeaderLength - 1)]
    public void BytesCutShortAreNotARecord(int keeping)
    {
        byte[] written = CaptionRecordFormat.Written(new CaptionRecord(1440, 1080, TimeSpan.Zero, [new CaptionCue(1, Lower)]));

        Assert.Null(CaptionRecordFormat.Read(written.AsSpan(0, keeping)));
        Assert.Null(CaptionRecordFormat.Read(written.AsSpan(0, written.Length - 1)));
    }

    [Fact]
    public void BytesThisFormatDidNotWriteAreNotARecord()
    {
        byte[] written = CaptionRecordFormat.Written(new CaptionRecord(1440, 1080, TimeSpan.Zero, []));
        byte[] otherVersion = [.. written];
        otherVersion[8] = 2;

        Assert.Null(CaptionRecordFormat.Read("not a record of captions at all"u8));
        Assert.Null(CaptionRecordFormat.Read(otherVersion));
        Assert.Null(CaptionRecordFormat.Read([.. written, 0]));
    }

    [Fact]
    public void AClearThatCarriesAPlacementIsNotOneThisFormatWrote()
    {
        byte[] written = CaptionRecordFormat.Written(new CaptionRecord(1440, 1080, TimeSpan.Zero, [new CaptionCue(5, null)]));
        written[CaptionRecordFormat.HeaderLength + 8 + 1] = 3;

        Assert.Null(CaptionRecordFormat.Read(written));
    }

    private static void AssertSame(CaptionPlacement expected, CaptionPlacement actual)
    {
        Assert.Equal((expected.Left, expected.Top, expected.Width, expected.Height), (actual.Left, actual.Top, actual.Width, actual.Height));
        Assert.Equal(expected.Png.ToArray(), actual.Png.ToArray());
    }
}
