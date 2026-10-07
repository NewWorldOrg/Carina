using Carina.Domain.Captions;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class CaptionPresenceTests
{
    private const long Second = CaptionCue.Hertz;

    private static readonly TimeSpan Begins = TimeSpan.FromSeconds(4100.25);

    [Fact(DisplayName = "a second shows captions while a picture is on screen at any moment of it, on the recording's own time")]
    public void ASecondShowsCaptionsWhileAPictureIsOnScreen()
    {
        CaptionRecord record = Record(Shown(4103.75), Cleared(4105.5), Shown(4108.25), Cleared(4108.5));

        LearningDataPart part = Assert.Single(CaptionPresence.Parts(record, TimeSpan.FromSeconds(10)));

        Assert.Equal(LearningDataKind.CaptionPresence, part.Kind);
        Assert.Equal(0, part.Index);
        Assert.Equal(Seconds(10, 3, 4, 5, 8), part.Captions.ToArray());
    }

    [Fact(DisplayName = "the captions are placed the way they are placed over the recording played as it is")]
    public void TheCaptionsArePlacedAsOverTheRecordingPlayedAsItIs()
    {
        CaptionRecord record = Record(Shown(4103.75), Cleared(4105.5), Shown(4108.25), Cleared(4108.5));
        CaptionWindow played = CaptionWindow.Of(record, record.StartsAt, null, TimeSpan.Zero);
        byte[] shown = CaptionPresence.Parts(record, TimeSpan.FromSeconds(10))[0].Captions.ToArray();

        foreach (PlacedCaption caption in played.Captions.Where(caption => caption.Picture is not null))
        {
            Assert.Equal(CaptionPresence.Shown, shown[(int)caption.At.TotalSeconds]);
        }
    }

    [Fact(DisplayName = "a picture before the file's clock begins is moved up to the recording's head")]
    public void APictureBeforeTheClockBeginsIsMovedUpToTheHead()
    {
        CaptionRecord record = Record(Shown(4099.5), Cleared(4101.75));

        Assert.Equal(Seconds(4, 0, 1), CaptionPresence.Parts(record, TimeSpan.FromSeconds(4))[0].Captions.ToArray());
    }

    [Fact(DisplayName = "a picture shown on a clock that began just before it came around is placed on the recording's own time")]
    public void APictureOnAClockThatCameAroundIsPlaced()
    {
        CaptionRecord record = new(1440, 1080, TimeSpan.FromSeconds(-2.5), [Shown(1.0), Cleared(2.0)]);

        Assert.Equal(Seconds(5, 3, 4), CaptionPresence.Parts(record, TimeSpan.FromSeconds(5))[0].Captions.ToArray());
    }

    [Fact(DisplayName = "the last picture stays to the end, and a picture replaced at the same moment shows nothing")]
    public void TheLastPictureStaysToTheEnd()
    {
        CaptionRecord record = Record(Shown(4101.25), Cleared(4101.25), Shown(4104.5));

        Assert.Equal(Seconds(6, 4, 5), CaptionPresence.Parts(record, TimeSpan.FromSeconds(6))[0].Captions.ToArray());
    }

    [Fact(DisplayName = "a picture past the recording's length is left out, and a part second at the end is a second")]
    public void APicturePastTheLengthIsLeftOut()
    {
        CaptionRecord record = Record(Shown(4102.5), Cleared(4103.5), Shown(4106.5), Cleared(4107));

        Assert.Equal(Seconds(5, 2, 3), CaptionPresence.Parts(record, TimeSpan.FromSeconds(4.5))[0].Captions.ToArray());
    }

    [Fact(DisplayName = "the seconds are cut into the chunks of the learning data, the last one short")]
    public void TheSecondsAreCutIntoChunks()
    {
        TimeSpan crossing = LearningData.ChunkStarts(1) + TimeSpan.FromSeconds(0.5);
        CaptionRecord record = Record(Shown(Begins.TotalSeconds + crossing.TotalSeconds - 1), Cleared(Begins.TotalSeconds + crossing.TotalSeconds + 1));

        IReadOnlyList<LearningDataPart> parts = CaptionPresence.Parts(record, LearningData.ChunkStarts(2) + TimeSpan.FromSeconds(30));

        Assert.Equal([0, 1, 2], parts.Select(part => part.Index));
        Assert.Equal([LearningData.ChunkSeconds, LearningData.ChunkSeconds, 30], parts.Select(part => part.Count));
        Assert.Equal(CaptionPresence.Shown, parts[0].Captions[^1]);
        Assert.Equal([CaptionPresence.Shown, CaptionPresence.Shown, CaptionPresence.Hidden], parts[1].Captions[..3].ToArray());
        Assert.Equal(1, parts[0].Captions.ToArray().Count(second => second is CaptionPresence.Shown));
        Assert.DoesNotContain(CaptionPresence.Shown, parts[2].Captions.ToArray());
    }

    [Fact(DisplayName = "a record with no picture at all says no second shows captions, and nothing read gives nothing")]
    public void ARecordWithNoPictureSaysNoSecondShowsCaptions()
    {
        LearningDataPart part = Assert.Single(CaptionPresence.Parts(Record(), TimeSpan.FromSeconds(3)));

        Assert.Equal(Seconds(3), part.Captions.ToArray());
        Assert.Empty(CaptionPresence.Parts(Record(Shown(4101)), TimeSpan.Zero));
    }

    [Fact(DisplayName = "a part of whether captions are shown holds no more than a chunk's seconds, each shown or not")]
    public void APartHoldsNoMoreThanAChunkEachShownOrNot()
    {
        Assert.Throws<ArgumentException>(() => LearningDataPart.OfCaptions(0, new byte[LearningData.ChunkSeconds + 1]));
        Assert.Throws<ArgumentException>(() => LearningDataPart.OfCaptions(0, [0, 1, 2]));
        Assert.Throws<ArgumentException>(() => LearningDataPart.OfCaptions(-1, [0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => CaptionPresence.Parts(Record(), TimeSpan.FromSeconds(-1)));
    }

    private static byte[] Seconds(int length, params int[] shown)
    {
        byte[] seconds = new byte[length];

        foreach (int second in shown)
        {
            seconds[second] = CaptionPresence.Shown;
        }

        return seconds;
    }

    private static CaptionRecord Record(params CaptionCue[] cues) => new(1440, 1080, Begins, cues);

    private static CaptionCue Shown(double seconds) => new((long)Math.Round(seconds * Second), new CaptionPlacement(0, 900, 100, 40, new byte[] { 0x89, 1 }));

    private static CaptionCue Cleared(double seconds) => new((long)Math.Round(seconds * Second), null);
}
