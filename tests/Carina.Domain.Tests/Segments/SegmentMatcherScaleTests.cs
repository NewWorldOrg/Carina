using System.Diagnostics;
using System.Runtime.InteropServices;

using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using Xunit.Abstractions;

namespace Carina.Domain.Tests.Segments;

[CollectionDefinition(nameof(SegmentMatcherScale), DisableParallelization = true)]
public sealed class SegmentMatcherScale;

[Trait("Category", "Scale")]
[Collection(nameof(SegmentMatcherScale))]
public sealed class SegmentMatcherScaleTests(ITestOutputHelper output)
{
    private const int Hours = 170;

    private const int MinutesEach = 30;

    private const int FramesEach = MinutesEach * 60 * 1000 / 16;

    private const int Recordings = Hours * 60 / MinutesEach;

    private const int AdvertFrames = 938;

    private const int MeasuredRuns = 3;

    private const string WhyTheProcessorClockIsTheGate =
        "The suite runs several test hosts at once, so the wall clock says as much about how busy the machine was as "
        + "about the matching. The gate is the processor time the matching spent, which does not move with the load: "
        + "the collection runs alone in this assembly, a warm run is thrown away first, and the quickest of several runs "
        + "is taken. The wall clock is printed beside it rather than asserted. Making the synthetic fingerprints is not "
        + "measured; the matching reads them from memory, as the job does once it has read them out of the database.";

    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    [Fact(DisplayName = "one recording is matched against 170 hours of fingerprints within ten seconds on one core")]
    public void OneRecordingIsMatchedAgainst170HoursInsideTheBudget()
    {
        uint[] archive = Random(1, Recordings * FramesEach);
        RecordedSound[] origins = [.. Enumerable.Range(0, Recordings).Select(_ => new RecordedSound(RecordingId.New()))];
        uint[] heard = Random(2, FramesEach);
        int[] shared = [17, 151, 299];

        for (int advert = 0; advert < shared.Length; advert++)
        {
            archive.AsSpan((shared[advert] * FramesEach) + 40_000, AdvertFrames).CopyTo(heard.AsSpan(10_000 + (advert * 20_000), AdvertFrames));
        }

        heard.AsSpan(10_000, AdvertFrames).CopyTo(heard.AsSpan(90_000, AdvertFrames));

        SoundTrack track = SoundTrack.Of(heard, []);
        RecordedSound itself = new(RecordingId.New());

        Matched(track, archive, origins, itself);

        List<TimeSpan> onTheWallClock = [];
        List<TimeSpan> onTheProcessorClock = [];
        List<SoundMatch> matches = [];

        for (int run = 0; run < MeasuredRuns; run++)
        {
            TimeSpan spentBefore = Process.GetCurrentProcess().TotalProcessorTime;
            long started = Stopwatch.GetTimestamp();
            matches = Matched(track, archive, origins, itself);
            onTheWallClock.Add(Stopwatch.GetElapsedTime(started));
            onTheProcessorClock.Add(Process.GetCurrentProcess().TotalProcessorTime - spentBefore);
        }

        TimeSpan quickest = onTheProcessorClock.Min();

        output.WriteLine($"{MinutesEach} minutes matched against {Recordings} recordings, {Hours} hours: {matches.Count} matches.");
        output.WriteLine("processor: " + string.Join(", ", onTheProcessorClock.Select(took => $"{took.TotalMilliseconds:F0} ms")));
        output.WriteLine("wall: " + string.Join(", ", onTheWallClock.Select(took => $"{took.TotalMilliseconds:F0} ms")));
        output.WriteLine(WhyTheProcessorClockIsTheGate);

        Assert.Equal<SoundOrigin>([origins[shared[0]], itself, origins[shared[1]], origins[shared[2]], origins[shared[0]]], [.. matches.Select(match => match.Other)]);
        Assert.True(quickest > TimeSpan.Zero, "the processor clock read nothing, which is no reading at all rather than a fast one.");
        Assert.True(
            quickest < Budget,
            $"the quickest of {MeasuredRuns} runs spent {quickest.TotalMilliseconds:F0} ms of processor time matching one recording "
            + $"against {Hours} hours, over the {Budget.TotalSeconds:F0} s budget.");
    }

    private static List<SoundMatch> Matched(SoundTrack track, uint[] archive, RecordedSound[] origins, RecordedSound itself)
    {
        SegmentMatcher matcher = SegmentMatcher.For(track);
        List<SoundMatch> matches = [];

        for (int recording = 0; recording < origins.Length; recording++)
        {
            matches.AddRange(matcher.Against(origins[recording], archive.AsSpan(recording * FramesEach, FramesEach), []));
        }

        matches.AddRange(matcher.AgainstItself(itself));

        return [.. matches.OrderBy(match => match.Heard.Starts)];
    }

    private static uint[] Random(int seed, int frames)
    {
        Random random = new(seed);
        uint[] fingerprints = new uint[frames];

        random.NextBytes(MemoryMarshal.AsBytes(fingerprints.AsSpan()));

        return fingerprints;
    }
}
