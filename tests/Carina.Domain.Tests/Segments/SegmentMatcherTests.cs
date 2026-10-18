using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentMatcherTests
{
    private static readonly TimeSpan Slack = TimeSpan.FromMilliseconds(300);

    private static readonly RecordedSound Other = new(RecordingId.New());

    private static readonly RecordedSound Itself = new(RecordingId.New());

    private static readonly short[] Advert = SoundScenes.Piece(100, 15);

    [Fact(DisplayName = "the same 15 seconds laid into two recordings at different places is found in both")]
    public void TheSamePieceInTwoRecordingsIsFound()
    {
        SoundTrack heard = SoundScenes.Track(SoundScenes.Laid(SoundScenes.Programme(1, 60), Advert, 10));
        SoundTrack other = SoundScenes.Track(SoundScenes.Laid(SoundScenes.Programme(2, 60), Advert, 37));

        SoundMatch match = Assert.Single(SegmentMatcher.For(heard).Against(Other, other.Fingerprints, []));

        Near(10, 25, match.Heard);
        Near(37, 52, match.There);
        Assert.Equal(Other, match.Other);
        Assert.InRange(match.BitErrorRate, 0, 0.1);
    }

    [Fact(DisplayName = "the same piece is found though one copy is half as loud and carries a little noise")]
    public void TheSamePieceIsFoundThroughGainAndNoise()
    {
        SoundTrack heard = SoundScenes.Track(SoundScenes.Laid(SoundScenes.Programme(1, 60), Advert, 10));
        SoundTrack other = SoundScenes.Track(SoundScenes.Laid(SoundScenes.Programme(2, 60), Advert, 37, gain: 0.5, noise: 50, noiseSeed: 7));

        SoundMatch match = Assert.Single(SegmentMatcher.For(heard).Against(Other, other.Fingerprints, []));

        Near(10, 25, match.Heard);
        Near(37, 52, match.There);
        Assert.InRange(match.BitErrorRate, 0.01, SegmentMatcher.MostBitErrors);
    }

    [Fact(DisplayName = "two recordings sharing nothing do not match")]
    public void RecordingsSharingNothingDoNotMatch()
    {
        SoundTrack heard = SoundScenes.Track(SoundScenes.Programme(1, 60));
        SoundTrack other = SoundScenes.Track(SoundScenes.Programme(2, 60));

        Assert.Empty(SegmentMatcher.For(heard).Against(Other, other.Fingerprints, []));
    }

    [Fact(DisplayName = "silence in one recording does not match silence in another")]
    public void SilenceDoesNotMatchSilence()
    {
        SoundTrack heard = SoundScenes.Track(SoundScenes.Laid(SoundScenes.Programme(1, 60), SoundScenes.Silence(20), 10));
        SoundTrack other = SoundScenes.Track(SoundScenes.Laid(SoundScenes.Programme(2, 60), SoundScenes.Silence(20), 30));

        Assert.Empty(SegmentMatcher.For(heard).Against(Other, other.Fingerprints, []));
    }

    [Fact(DisplayName = "a steady tone in one recording does not match the same tone in another")]
    public void ASteadyToneDoesNotMatch()
    {
        SoundTrack heard = SoundScenes.Track(SoundScenes.Laid(SoundScenes.Programme(1, 60), SoundScenes.Tone(440, 20), 10));
        SoundTrack other = SoundScenes.Track(SoundScenes.Laid(SoundScenes.Programme(2, 60), SoundScenes.Tone(440, 20), 30));

        Assert.Empty(SegmentMatcher.For(heard).Against(Other, other.Fingerprints, []));
    }

    [Fact(DisplayName = "a piece heard twice in one recording is found by matching the recording against itself")]
    public void APieceHeardTwiceInOneRecordingIsFoundInIt()
    {
        short[] twice = SoundScenes.Laid(SoundScenes.Laid(SoundScenes.Programme(1, 90), Advert, 10), Advert, 60);

        SoundMatch match = Assert.Single(SegmentMatcher.For(SoundScenes.Track(twice)).AgainstItself(Itself));

        Near(10, 25, match.Heard);
        Near(60, 75, match.There);
        Assert.Equal(Itself, match.Other);
    }

    [Fact(DisplayName = "a recording holding no piece twice does not match itself")]
    public void ARecordingWithNoRepeatDoesNotMatchItself()
    {
        Assert.Empty(SegmentMatcher.For(SoundScenes.Track(SoundScenes.Programme(1, 90))).AgainstItself(Itself));
    }

    [Fact(DisplayName = "a piece heard twice in one recording is found at both places against another recording")]
    public void BothPlacesAreFoundAgainstAnotherRecording()
    {
        short[] twice = SoundScenes.Laid(SoundScenes.Laid(SoundScenes.Programme(1, 90), Advert, 10), Advert, 60);
        SoundTrack other = SoundScenes.Track(SoundScenes.Laid(SoundScenes.Programme(2, 60), Advert, 37));

        IReadOnlyList<SoundMatch> matches = SegmentMatcher.For(SoundScenes.Track(twice)).Against(Other, other.Fingerprints, []);

        Assert.Equal(2, matches.Count);
        Near(10, 25, matches[0].Heard);
        Near(60, 75, matches[1].Heard);
        Assert.All(matches, match => Near(37, 52, match.There));
    }

    [Fact(DisplayName = "a catalog entry's fingerprint is matched like a recording's")]
    public void AnEntryIsMatchedLikeARecording()
    {
        SoundTrack source = SoundScenes.Track(SoundScenes.Laid(SoundScenes.Programme(2, 60), Advert, 37));
        uint[] fingerprint = source.Slice(new SoundSpan(SoundScenes.Seconds(37), SoundScenes.Seconds(52)));
        EntrySound entry = new(CatalogEntryId.New());
        SoundTrack heard = SoundScenes.Track(SoundScenes.Laid(SoundScenes.Programme(1, 60), Advert, 10));

        SoundMatch match = Assert.Single(SegmentMatcher.For(heard).Against(entry, fingerprint, []));

        Near(10, 25, match.Heard);
        Near(0, 15, match.There);
        Assert.Equal(entry, match.Other);
    }

    [Fact(DisplayName = "the stretches of the other recording already explained are not matched")]
    public void ExplainedStretchesAreNotMatched()
    {
        SoundTrack heard = SoundScenes.Track(SoundScenes.Laid(SoundScenes.Programme(1, 60), Advert, 10));
        SoundTrack other = SoundScenes.Track(SoundScenes.Laid(SoundScenes.Programme(2, 60), Advert, 37));
        SoundSpan explained = new(SoundScenes.Seconds(36), SoundScenes.Seconds(53));

        Assert.Empty(SegmentMatcher.For(heard).Against(Other, other.Fingerprints, [explained]));
    }

    [Fact(DisplayName = "a piece shorter than four seconds is not a match")]
    public void APieceShorterThanFourSecondsIsNotAMatch()
    {
        short[] brief = SoundScenes.Piece(101, 3);
        SoundTrack heard = SoundScenes.Track(SoundScenes.Laid(SoundScenes.Programme(1, 60), brief, 10));
        SoundTrack other = SoundScenes.Track(SoundScenes.Laid(SoundScenes.Programme(2, 60), brief, 37));

        Assert.Empty(SegmentMatcher.For(heard).Against(Other, other.Fingerprints, []));
    }

    [Fact(DisplayName = "a shared stretch longer than an entry can hold is the same programme heard again, not a repeated piece")]
    public void AStretchLongerThanAnEntryIsNotAMatch()
    {
        short[] rerun = SoundScenes.Piece(102, 200);
        SoundTrack heard = SoundScenes.Track(SoundScenes.Laid(SoundScenes.Programme(1, 240), rerun, 10));
        SoundTrack other = SoundScenes.Track(SoundScenes.Laid(SoundScenes.Programme(2, 240), rerun, 30));

        Assert.Empty(SegmentMatcher.For(heard).Against(Other, other.Fingerprints, []));
    }

    [Fact(DisplayName = "a vote at the other sound's first frame from the offset beside the candidate's does not reach before that frame")]
    public void AVoteAtTheFirstFrameFromTheNextOffsetStaysInReach()
    {
        uint[] heard = Random(1, 5_000);
        uint[] there = Random(2, 3_000);
        heard[999] = heard[1_000];
        heard.AsSpan(1_000, 938).CopyTo(there);

        SoundMatch match = Assert.Single(SegmentMatcher.For(SoundTrack.Of(heard, [])).Against(Other, there, []));

        Assert.Equal(SegmentMatcher.Frame * 1_000, match.Heard.Starts);
        Assert.Equal(TimeSpan.Zero, match.There.Starts);
    }

    [Fact(DisplayName = "a vote at the other sound's last frame from the offset before the candidate's does not reach past that frame")]
    public void AVoteAtTheLastFrameFromThePreviousOffsetStaysInReach()
    {
        uint[] heard = Random(1, 5_000);
        uint[] there = Random(2, 3_000);
        heard.AsSpan(1_000, 938).CopyTo(there.AsSpan(3_000 - 938));
        heard[1_938] = heard[1_937];

        SoundMatch match = Assert.Single(SegmentMatcher.For(SoundTrack.Of(heard, [])).Against(Other, there, []));

        Assert.Equal(SegmentMatcher.Frame * 1_938, match.Heard.Ends);
        Assert.Equal(SegmentMatcher.Frame * 3_000, match.There.Ends);
    }

    private static uint[] Random(int seed, int frames)
    {
        Random random = new(seed);

        return [.. Enumerable.Range(0, frames).Select(_ => (uint)random.NextInt64(1, uint.MaxValue))];
    }

    private static void Near(double starts, double ends, SoundSpan span)
    {
        Assert.InRange(span.Starts, SoundScenes.Seconds(starts) - Slack, SoundScenes.Seconds(starts) + Slack);
        Assert.InRange(span.Ends, SoundScenes.Seconds(ends) - Slack, SoundScenes.Seconds(ends) + Slack);
    }
}
