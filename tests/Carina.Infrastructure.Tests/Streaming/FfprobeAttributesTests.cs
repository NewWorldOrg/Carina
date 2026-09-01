using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class FfprobeAttributesTests
{
    [Fact]
    public void TheOrdinaryBroadcastShapeIsReadWhole()
    {
        StreamAttributeReading reading = FfprobeAttributes.Read(Probes.Recorded(Probes.BroadcastHd));

        Assert.True(reading.Measured);
        Assert.Null(reading.Fault);
        Assert.Equal(1440, reading.Attributes.Size.Width);
        Assert.Equal(1080, reading.Attributes.Size.Height);
        Assert.Equal(ScanType.Interlaced, reading.Attributes.Scan);
        Assert.Equal("30000/1001", reading.Attributes.Rate.ToString());
        Assert.Equal(AudioMode.Stereo, reading.Attributes.Audio);
    }

    [Fact]
    public void TheSeventhChannelInAHundredIsReadAsWhatItIs()
    {
        StreamAttributeReading reading = FfprobeAttributes.Read(Probes.Recorded(Probes.BroadcastSd));

        Assert.True(reading.Measured);
        Assert.Equal(720, reading.Attributes.Size.Width);
        Assert.Equal(480, reading.Attributes.Size.Height);
        Assert.Equal(ScanType.Interlaced, reading.Attributes.Scan);
    }

    [Fact]
    public void AProgressivePictureIsNotDeinterlacedOnSuspicion()
    {
        StreamAttributeReading reading = FfprobeAttributes.Read(Probes.Recorded(Probes.Progressive));

        Assert.True(reading.Measured);
        Assert.Equal(1920, reading.Attributes.Size.Width);
        Assert.Equal(ScanType.Progressive, reading.Attributes.Scan);
        Assert.Equal("30/1", reading.Attributes.Rate.ToString());
    }

    [Fact]
    public void TheFirstPictureInAMultiplexIsTheAnswerAndTheOthersAreDeclared()
    {
        StreamAttributeReading reading = FfprobeAttributes.Read(Probes.Recorded(Probes.Multiplex));

        Assert.Equal(1440, reading.Attributes.Size.Width);
        Assert.Equal(1080, reading.Attributes.Size.Height);
        Assert.True(reading.SeveralVideoDescriptions);
    }

    [Fact]
    public void OnePictureListedFourTimesIsStillOnePicture()
    {
        Assert.False(FfprobeAttributes.Read(Probes.Recorded(Probes.BroadcastHd)).SeveralVideoDescriptions);
    }

    [Fact]
    public void TheSoundsFrameRateIsNotThePicturesFrameRate()
    {
        string recorded = Probes.Recorded(Probes.BroadcastHd);

        Assert.Contains("r_frame_rate=0/0", recorded, StringComparison.Ordinal);
        Assert.Equal("30000/1001", FfprobeAttributes.Read(recorded).Attributes.Rate.ToString());
    }

    [Fact]
    public void MonoIsReadAsMono()
    {
        Assert.Equal(AudioMode.Mono, FfprobeAttributes.Read(Probes.Recorded(Probes.Mono)).Attributes.Audio);
    }

    [Fact]
    public void SixChannelsAreReadAsSurround()
    {
        Assert.Equal(AudioMode.Surround, FfprobeAttributes.Read(Probes.Recorded(Probes.Surround)).Attributes.Audio);
    }

    [Fact]
    public void TwoChannelsWhoseLayoutTheStreamNeverSettledAreReadAsDualMono()
    {
        StreamAttributeReading reading = FfprobeAttributes.Read(Probes.Recorded(Probes.UndeterminedLayout));

        Assert.Equal(AudioMode.DualMono, reading.Attributes.Audio);
        Assert.Contains(StreamAttribute.Resolution, reading.FellBackOn);
    }

    [Fact]
    public void SoundThatCallsItselfStereoIsReadAsStereoWhateverTheTwoChannelsCarry()
    {
        Assert.Equal(
            AudioMode.Stereo,
            FfprobeAttributes.Read("codec_type=audio\nchannels=2\nchannel_layout=stereo\n").Attributes.Audio);
    }

    [Fact]
    public void AnEmptyAnswerIsNotAnAnswer()
    {
        StreamAttributeReading reading = FfprobeAttributes.Read(string.Empty);

        Assert.Equal(StreamProbeFault.SaidNothing, reading.Fault);
        Assert.False(reading.Measured);
        Assert.Equal(StreamAttributes.SafeSide, reading.Attributes);
    }

    [Fact]
    public void APictureWithNoFieldOrderIsDeinterlacedAndSaysThatItWasGuessedAt()
    {
        StreamAttributeReading reading = FfprobeAttributes.Read(
            "codec_type=video\nwidth=1440\nheight=1080\nr_frame_rate=30000/1001\n");

        Assert.Equal(ScanType.Interlaced, reading.Attributes.Scan);
        Assert.True(reading.FellBack(StreamAttribute.Scan));
        Assert.False(reading.FellBack(StreamAttribute.Resolution));
        Assert.False(reading.Measured);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("N/A")]
    public void AFieldOrderTheProgrammeCouldNotSettleIsNotReadAsProgressive(string order)
    {
        StreamAttributeReading reading = FfprobeAttributes.Read(
            $"codec_type=video\nwidth=1440\nheight=1080\nfield_order={order}\n");

        Assert.Equal(ScanType.Interlaced, reading.Attributes.Scan);
        Assert.True(reading.FellBack(StreamAttribute.Scan));
    }

    [Fact]
    public void ARateOfNothingOverNothingIsNoRate()
    {
        StreamAttributeReading reading = FfprobeAttributes.Read(
            "codec_type=video\nwidth=1440\nheight=1080\nfield_order=tt\nr_frame_rate=0/0\n");

        Assert.True(reading.FellBack(StreamAttribute.FrameRate));
        Assert.Equal(StreamAttributes.SafeSide.Rate, reading.Attributes.Rate);
    }

    [Fact]
    public void APictureWithNoSoundBesideItFallsBackOnlyOnTheSound()
    {
        StreamAttributeReading reading = FfprobeAttributes.Read(
            "codec_type=video\nwidth=1920\nheight=1080\nfield_order=progressive\nr_frame_rate=30/1\n");

        Assert.Equal([StreamAttribute.Audio], reading.FellBackOn);
        Assert.Equal(AudioMode.Stereo, reading.Attributes.Audio);
        Assert.Null(reading.Fault);
    }

    [Fact]
    public void WhatTheProgrammeSaidWhenItRefusedIsKept()
    {
        StreamAttributeReading reading = StreamAttributeReading.Refused(1, Probes.Recorded(Probes.Refused));

        Assert.Equal(StreamProbeFault.Refused, reading.Fault);
        Assert.Equal(1, reading.ExitCode);
        Assert.Contains("Invalid argument", reading.Note, StringComparison.Ordinal);
        Assert.Equal(StreamAttributes.SafeSide, reading.Attributes);
    }
}
