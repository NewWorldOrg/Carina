using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class FfprobeRecordsTests
{
    [Fact]
    public void OneProgrammeIsStillListedTwice()
    {
        Assert.Equal(4, FfprobeRecords.From(Probes.Recorded(Probes.BroadcastHd)).Count);
    }

    [Fact]
    public void TwoProgrammesPutFourVideoRecordsInFrontOfAReaderThatWantsOne()
    {
        IReadOnlyList<FfprobeRecord> records = FfprobeRecords.From(Probes.Recorded(Probes.Multiplex));

        Assert.Equal(8, records.Count);
        Assert.Equal(4, records.Count(record => record.Value("codec_type") is "video"));
    }

    [Fact]
    public void ARepeatedKeyIsWhereOneRecordEndsAndTheNextBegins()
    {
        IReadOnlyList<FfprobeRecord> records = FfprobeRecords.From(
            "codec_type=video\nwidth=1440\ncodec_type=audio\nchannels=2\n");

        Assert.Equal(2, records.Count);
        Assert.Equal("1440", records[0].Value("width"));
        Assert.Null(records[1].Value("width"));
        Assert.Equal("2", records[1].Value("channels"));
    }

    [Fact]
    public void TheProgrammeDoesNotAnswerInTheOrderItWasAsked()
    {
        string recorded = Probes.Recorded(Probes.BroadcastHd);

        Assert.Contains("codec_type", FfprobeInvocation.Entries, StringComparison.Ordinal);
        Assert.True(
            FfprobeInvocation.Entries.IndexOf("codec_type", StringComparison.Ordinal)
            < FfprobeInvocation.Entries.IndexOf("codec_name", StringComparison.Ordinal));
        Assert.True(
            recorded.IndexOf("codec_name", StringComparison.Ordinal)
            < recorded.IndexOf("codec_type", StringComparison.Ordinal));
    }

    [Fact]
    public void AStreamThatCarriesNoPictureCarriesNoWidthEither()
    {
        FfprobeRecord sound = FfprobeRecords
            .From(Probes.Recorded(Probes.BroadcastHd))
            .First(record => record.Value("codec_type") is "audio");

        Assert.Null(sound.Value("width"));
        Assert.Null(sound.Value("height"));
        Assert.Equal("stereo", sound.Value("channel_layout"));
    }

    [Fact]
    public void NothingIsReadOutOfNothing()
    {
        Assert.Empty(FfprobeRecords.From(string.Empty));
    }

    [Theory]
    [InlineData("no equals sign here\n")]
    [InlineData("=leading equals\n")]
    [InlineData("\n\n   \n")]
    public void ALineThatNamesNothingIsPassedOver(string output)
    {
        Assert.Empty(FfprobeRecords.From(output));
    }

    [Fact]
    public void AValueMayCarryTheSignThatSeparatesIt()
    {
        FfprobeRecord only = Assert.Single(FfprobeRecords.From("tag=a=b\n"));

        Assert.Equal("a=b", only.Value("tag"));
    }

    [Fact]
    public void CarriageReturnsAreNotPartOfTheValue()
    {
        FfprobeRecord only = Assert.Single(FfprobeRecords.From("width=1440\r\n"));

        Assert.Equal("1440", only.Value("width"));
    }
}
