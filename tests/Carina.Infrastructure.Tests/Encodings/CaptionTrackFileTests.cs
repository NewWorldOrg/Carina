using Carina.Domain.Captions;
using Carina.Infrastructure.Encodings;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class CaptionTrackFileTests
{
    private const long Second = 90_000;

    private static readonly TimeSpan Shift = TimeSpan.FromSeconds(100);

    private static readonly TimeSpan Length = TimeSpan.FromSeconds(60);

    [Fact]
    public void BrEd2019EachTextIsACueFromItsMomentLessTheShiftUntilTheNextChange()
    {
        string? written = CaptionTrackFile.Written(
            [new CaptionLine(108 * Second, "合成字幕\nCARINA"), new CaptionLine((109 * Second) + 45_000, "字"), new CaptionLine(111 * Second, null)],
            Shift,
            Length);

        Assert.Equal(
            "WEBVTT\n\n00:00:08.000 --> 00:00:09.500\n合成字幕\nCARINA\n\n00:00:09.500 --> 00:00:11.000\n字\n",
            written);
    }

    [Fact]
    public void BrEd2019ACueThatBeginsBeforeZeroBeginsAtZeroAndOneThatEndsBeforeItIsDropped()
    {
        string? written = CaptionTrackFile.Written(
            [new CaptionLine(98 * Second, "前"), new CaptionLine(99 * Second, null), new CaptionLine((99 * Second) + 45_000, "頭"), new CaptionLine(101 * Second, null)],
            Shift,
            Length);

        Assert.Equal("WEBVTT\n\n00:00:00.000 --> 00:00:01.000\n頭\n", written);
    }

    [Fact]
    public void BrEd2019ACueStillShownAtTheEndLastsToTheEndAndOnePastTheEndIsDropped()
    {
        string? written = CaptionTrackFile.Written(
            [new CaptionLine(150 * Second, "跨"), new CaptionLine(170 * Second, "後")],
            Shift,
            Length);

        Assert.Equal("WEBVTT\n\n00:00:50.000 --> 00:01:00.000\n跨\n", written);
    }

    [Fact]
    public void BrEd2019WithNoLengthKnownTheLastCueLastsFiveSeconds()
    {
        string? written = CaptionTrackFile.Written([new CaptionLine(3_700 * Second, "長")], Shift, null);

        Assert.Equal("WEBVTT\n\n01:00:00.000 --> 01:00:05.000\n長\n", written);
    }

    [Fact]
    public void BrEd2019WhatAPlayerWouldReadAsMarkupIsWrittenAsText()
    {
        string? written = CaptionTrackFile.Written([new CaptionLine(101 * Second, "<b>A & B</b> -->")], Shift, Length);

        Assert.Equal("WEBVTT\n\n00:00:01.000 --> 00:01:00.000\n&lt;b&gt;A &amp; B&lt;/b&gt; --&gt;\n", written);
    }

    [Fact]
    public void BrEd2019NoCueLeftIsNoTrack()
    {
        Assert.Null(CaptionTrackFile.Written([], Shift, Length));
        Assert.Null(CaptionTrackFile.Written([new CaptionLine(50 * Second, "前"), new CaptionLine(60 * Second, null)], Shift, Length));
        Assert.Null(CaptionTrackFile.Written([new CaptionLine(101 * Second, null)], Shift, Length));
    }

    [Fact]
    public void BrEd2019ANegativeShiftAddsToTheMomentsAsTheSameSubtraction()
    {
        string? written = CaptionTrackFile.Written(
            [new CaptionLine(-2 * Second, "負"), new CaptionLine(Second, null)],
            TimeSpan.FromSeconds(-3),
            Length);

        Assert.Equal("WEBVTT\n\n00:00:01.000 --> 00:00:04.000\n負\n", written);
    }
}
