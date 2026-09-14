using Carina.Domain.Encodings;
using Carina.Infrastructure.Encodings;

namespace Carina.Infrastructure.Tests.Encodings;

/// <summary>
/// The lines here are written the way ffmpeg 6.1 writes them, down to the space one filter leaves
/// after its colon and the next one does not. The times in them are made up; that the shapes are
/// the real tool's is held by the material test that runs it.
/// </summary>
public sealed class ChapterLogParsingTests
{
    [Fact(DisplayName = "a quiet stretch is one line saying where it began and another saying where it ended")]
    public void AQuietStretchIsTwoLines()
    {
        ChapterLog heard = Complaining(
            "[silencedetect @ 0x5643dcc08ac0] silence_start: 30.0267",
            "[silencedetect @ 0x5643dcc08ac0] silence_end: 30.4319 | silence_duration: 0.405146",
            "[silencedetect @ 0x5643dcc08ac0] silence_start: 90.0373",
            "[silencedetect @ 0x5643dcc08ac0] silence_end: 90.4426 | silence_duration: 0.405271");

        Assert.Equal(
            [
                new ChapterSpan(TimeSpan.FromSeconds(30.0267), TimeSpan.FromSeconds(30.4319)),
                new ChapterSpan(TimeSpan.FromSeconds(90.0373), TimeSpan.FromSeconds(90.4426)),
            ],
            heard.Silences);
        Assert.Empty(heard.Blacks);
        Assert.Empty(heard.Scenes);
    }

    [Fact(DisplayName = "a quiet stretch the source stopped in the middle of is no stretch, there being nothing to say where it ended")]
    public void AQuietStretchTheSourceStoppedInIsNoStretch()
        => Assert.Empty(Complaining("[silencedetect @ 0x5643dcc08ac0] silence_start: 129.8").Silences);

    [Fact(DisplayName = "an end without a beginning is not read as a stretch from nowhere")]
    public void AnEndWithoutABeginningIsNoStretch()
        => Assert.Empty(
            Complaining("[silencedetect @ 0x5643dcc08ac0] silence_end: 30.4319 | silence_duration: 0.405146").Silences);

    [Fact(DisplayName = "the dark is one line carrying where it began, where it ended and how long it lasted")]
    public void TheDarkIsOneLine()
    {
        ChapterLog seen =
            Complaining("[blackdetect @ 0x7fb680000ec0] black_start:31.4634 black_end:31.8638 black_duration:0.400401");

        Assert.Equal(
            [new ChapterSpan(TimeSpan.FromSeconds(31.4634), TimeSpan.FromSeconds(31.8638))],
            seen.Blacks);
    }

    [Fact(DisplayName = "how much the picture changed is the score printed under the frame it was measured on")]
    public void HowMuchThePictureChangedIsScoredUnderItsFrame()
    {
        ChapterLog seen = Saying(
            "frame:0    pts:2831703 pts_time:31.4634",
            "lavfi.black_start=31.4634",
            "lavfi.scene_score=1.000000",
            "frame:1    pts:2867739 pts_time:31.8638",
            "lavfi.black_end=31.8638",
            "lavfi.scene_score=0.418732");

        Assert.Equal(
            [
                new ChapterScene(TimeSpan.FromSeconds(31.4634), 1),
                new ChapterScene(TimeSpan.FromSeconds(31.8638), 0.418732),
            ],
            seen.Scenes);
        Assert.Empty(seen.Blacks);
        Assert.Empty(seen.Silences);
    }

    [Fact(DisplayName = "a score before any frame was named belongs to no moment and is left out")]
    public void AScoreBeforeAnyFrameBelongsToNoMoment()
        => Assert.Empty(Saying("lavfi.scene_score=0.9").Scenes);

    [Fact(DisplayName = "the two streams are read apart: what is said on one is not looked for on the other")]
    public void TheTwoStreamsAreReadApart()
    {
        ChapterLog crossed = new();
        crossed.Said("[silencedetect @ 0x5643dcc08ac0] silence_start: 30.0267");
        crossed.Said("[blackdetect @ 0x7fb680000ec0] black_start:31.4634 black_end:31.8638 black_duration:0.400401");
        crossed.Complained("lavfi.scene_score=1.000000");

        Assert.Empty(crossed.Silences);
        Assert.Empty(crossed.Blacks);
        Assert.Empty(crossed.Scenes);
    }

    [Fact(DisplayName = "what the run says about anything else is nothing to do with the breaks and is passed over")]
    public void WhatTheRunSaysAboutAnythingElseIsPassedOver()
    {
        ChapterLog heard = Complaining(
            "[h264 @ 0x5624353b4b80] non-existing PPS 0 referenced",
            "    Last message repeated 3 times",
            "[mpeg2video @ 0x56243531e200] Invalid frame dimensions 0x0.",
            "  Duration: 00:00:20.02, start: 30500.852667, bitrate: 2219 kb/s",
            "[out#0/null @ 0x556829056180] video:0kB audio:393172kB subtitle:0kB other streams:0kB",
            string.Empty);

        Assert.Empty(heard.Silences);
        Assert.Empty(heard.Blacks);
        Assert.Empty(heard.Scenes);
    }

    [Fact(DisplayName = "a moment or a score the line does not carry as a number is no reading at all")]
    public void AMomentThatIsNoNumberIsNoReading()
    {
        ChapterLog nonsense = new();
        nonsense.Complained("[silencedetect @ 0x1] silence_start: N/A");
        nonsense.Complained("[silencedetect @ 0x1] silence_end: 30.4");
        nonsense.Complained("[blackdetect @ 0x1] black_start:-1 black_end:2");
        nonsense.Said("frame:0    pts:0 pts_time:N/A");
        nonsense.Said("lavfi.scene_score=inf");

        Assert.Empty(nonsense.Silences);
        Assert.Empty(nonsense.Blacks);
        Assert.Empty(nonsense.Scenes);
    }

    [Fact(DisplayName = "a stretch that ends where it began, or before it, is no stretch")]
    public void AStretchThatEndsWhereItBeganIsNoStretch()
    {
        ChapterLog backwards = Complaining(
            "[silencedetect @ 0x1] silence_start: 30.0",
            "[silencedetect @ 0x1] silence_end: 30.0 | silence_duration: 0",
            "[blackdetect @ 0x1] black_start:31.5 black_end:31.5 black_duration:0");

        Assert.Empty(backwards.Silences);
        Assert.Empty(backwards.Blacks);
    }

    [Fact(DisplayName = "nothing is read out of a line that was never handed over")]
    public void NothingIsReadOutOfALineThatWasNeverHandedOver()
    {
        ChapterLog log = new();

        Assert.Throws<ArgumentNullException>(() => log.Said(null!));
        Assert.Throws<ArgumentNullException>(() => log.Complained(null!));
    }

    private static ChapterLog Complaining(params string[] lines)
    {
        ChapterLog log = new();

        foreach (string line in lines)
        {
            log.Complained(line);
        }

        return log;
    }

    private static ChapterLog Saying(params string[] lines)
    {
        ChapterLog log = new();

        foreach (string line in lines)
        {
            log.Said(line);
        }

        return log;
    }
}
