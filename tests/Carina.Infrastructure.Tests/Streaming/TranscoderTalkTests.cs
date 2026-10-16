using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class TranscoderTalkTests
{
    [Theory]
    [InlineData("[error] Error parsing options for output file -.", "Error parsing options for output file -.")]
    [InlineData("[fatal] Stream map 'p:1040:v:0' matches no streams.", "Stream map 'p:1040:v:0' matches no streams.")]
    [InlineData("[panic] Assertion failed", "Assertion failed")]
    [InlineData(
        "[vist#0:0/mpeg2video @ 0x55912096e900] [error] Error submitting packet to decoder: Invalid data found when processing input",
        "[vist#0:0/mpeg2video @ 0x55912096e900] Error submitting packet to decoder: Invalid data found when processing input")]
    public void AnErrorIsKeptAsItWouldHaveBeenSaidWithoutItsMark(string said, string kept)
    {
        Assert.Equal(kept, new TranscoderTalk().Hear(said).Kept);
    }

    [Theory]
    [InlineData("[info] Stream mapping:")]
    [InlineData("[info]   Stream #0:0 -> #0:0 (mpeg2video (native) -> h264 (libx264))")]
    [InlineData("[mpegts @ 0x560b3a84ea40] [warning] Could not find codec parameters for stream 4 (Video: h264): unspecified size")]
    [InlineData("[mpegts @ 0x560b3a84ea40] [verbose] max_analyze_duration 250000 reached at 250000 microseconds st:1")]
    public void WhatIsLessThanAnErrorIsNotKept(string said)
    {
        Assert.Null(new TranscoderTalk().Hear(said).Kept);
    }

    [Fact]
    public void ALineWithoutAMarkGoesWithTheLineBeforeIt()
    {
        TranscoderTalk talk = new();

        Assert.Null(talk.Hear("[mpegts @ 0x1] [warning] Could not find codec parameters for stream 0").Kept);
        Assert.Null(talk.Hear("Consider increasing the value for the 'analyzeduration' and 'probesize' options").Kept);
        Assert.NotNull(talk.Hear("[fatal] Stream map 'p:1040:v:0' matches no streams.").Kept);
        Assert.Equal("To ignore this, add a trailing '?' to the map.", talk.Hear("To ignore this, add a trailing '?' to the map.").Kept);
    }

    [Fact]
    public void AProgrammeAskedForErrorsAloneMarksNothingAndEveryLineItSaysIsKept()
    {
        TranscoderTalk talk = new();

        Assert.Equal("Invalid data found when processing input", talk.Hear("Invalid data found when processing input").Kept);
        Assert.Equal("[mpegts @ 0x1] Packet corrupt", talk.Hear("[mpegts @ 0x1] Packet corrupt").Kept);
        Assert.False(talk.Hear("Input #0, mpegts, from 'pipe:0':").InputRead);
    }

    [Fact]
    public void TheInputIsReadWhenTheProgrammeSaysWhatItHolds()
    {
        TranscoderTalk talk = new();

        Assert.False(talk.Hear("[mpegts @ 0x1] [warning] Could not find codec parameters for stream 0").InputRead);
        Assert.True(talk.Hear("[info] Input #0, mpegts, from 'pipe:0':").InputRead);
        Assert.False(talk.Hear("[info]   Duration: N/A, start: 6.126700, bitrate: N/A").InputRead);
        Assert.False(talk.Hear("[error] Input #0 is nothing an error says").InputRead);
    }
}
