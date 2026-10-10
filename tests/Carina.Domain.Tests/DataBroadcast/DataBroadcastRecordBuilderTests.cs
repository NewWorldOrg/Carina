using Carina.Domain.DataBroadcast;

namespace Carina.Domain.Tests.DataBroadcast;

public sealed class DataBroadcastRecordBuilderTests
{
    [Fact(DisplayName = "BR-BS-002: the record keeps the versions the catalog no longer holds valid")]
    public void TheRecordKeepsVersionsNoLongerValid()
    {
        DataBroadcastRecord record = Read(
            (Carousels.Carried(), 0),
            (Carousels.Listing(Carousels.Entry, (0, 1)), 0),
            (Carousels.Completed(Carousels.Entry, 0, 1), 100),
            (Carousels.Listing(Carousels.Entry, 1, [0], (0, 2)), 200),
            (Carousels.Completed(Carousels.Entry, 0, 2), 300));

        RecordedCarousel carousel = Assert.Single(record.Carousels);
        Assert.Equal([(1, 100L), (2, 300L)], carousel.Versions.Select(version => (version.Version, version.FirstSeen)));
        Assert.Equal(Carousels.Entry, record.EntryTag);
        Assert.False(record.Incomplete);
    }

    [Fact(DisplayName = "BR-BD-005: a version is last seen when a listing last held it valid")]
    public void AVersionIsLastSeenWhenAListingLastHeldItValid()
    {
        DataBroadcastRecord record = Read(
            (Carousels.Carried(), 0),
            (Carousels.Listing(Carousels.Entry, (0, 1), (1, 1)), 0),
            (Carousels.Completed(Carousels.Entry, 0, 1), 100),
            (Carousels.Listing(Carousels.Entry, 1, [1], (0, 1), (1, 2)), 500),
            (Carousels.Listing(Carousels.Entry, 1, [0], (0, 2), (1, 2)), 900));

        ModuleVersion version = Assert.Single(Assert.Single(record.Carousels).Versions);
        Assert.Equal((100L, 500L), (version.FirstSeen, version.LastSeen));
    }

    [Fact(DisplayName = "BR-BD-005: the same tag, module and version is held once")]
    public void TheSameVersionPutTogetherAgainIsHeldOnce()
    {
        DataBroadcastRecord record = Read(
            (Carousels.Carried(), 0),
            (Carousels.Listing(Carousels.Entry, (0, 1)), 0),
            (Carousels.Completed(Carousels.Entry, 0, 1), 100),
            (Carousels.Listing(Carousels.Entry, 1, [0], (0, 1)), 200),
            (Carousels.Completed(Carousels.Entry, 0, 1), 300));

        ModuleVersion version = Assert.Single(Assert.Single(record.Carousels).Versions);
        Assert.Equal(100L, version.FirstSeen);
    }

    [Fact(DisplayName = "BR-BD-005: the record holds every event message and the download of every carousel")]
    public void TheRecordHoldsEveryEventAndEveryDownload()
    {
        DataBroadcastRecord record = Read(
            (Carousels.Carried(), 0),
            (Carousels.Listing(Carousels.Entry, 7, [], (0, 1)), 0),
            (Carousels.Listing(Carousels.Other, 9, [], (0, 1)), 0),
            (new CarouselSignal.EventTimed(Carousels.Event(2, 600)), 500),
            (new CarouselSignal.EventTimed(Carousels.Event(1, 400)), 300));

        Assert.Equal([(Carousels.Entry, 7u), (Carousels.Other, 9u)], record.Carousels.Select(carousel => (carousel.Tag, carousel.DownloadId)));
        Assert.Equal([1, 2], record.Events.Select(message => message.Id));
        Assert.Equal(0, record.Modules);
    }

    [Fact(DisplayName = "BR-BD-005: the versions still valid when the recording ends are last seen at its end")]
    public void TheVersionsStillValidAtTheEndAreLastSeenThere()
    {
        DataBroadcastRecord record = ReadFrom(
            0,
            1_000,
            (Carousels.Carried(), 0),
            (Carousels.Listing(Carousels.Entry, (0, 1), (1, 1)), 0),
            (Carousels.Completed(Carousels.Entry, 0, 1), 100),
            (Carousels.Completed(Carousels.Entry, 1, 1), 200),
            (Carousels.Listing(Carousels.Entry, 1, [], (0, 1)), 300));

        Assert.Equal(
            [(0, 1_000L), (1, 200L)],
            Assert.Single(record.Carousels).Versions.Select(version => (version.ModuleId, version.LastSeen)));
    }

    [Fact]
    public void NothingIsValidAtTheEndOfARecordingWhoseDataBroadcastWentAway()
    {
        DataBroadcastRecordBuilder builder = new();
        CarouselState state = new();

        foreach ((CarouselSignal signal, long at) in new (CarouselSignal, long)[]
                 {
                     (Carousels.Carried(), 0),
                     (Carousels.Listing(Carousels.Entry, (0, 1)), 0),
                     (Carousels.Completed(Carousels.Entry, 0, 1), 100),
                     (new CarouselSignal.NotCarried(), 200),
                 })
        {
            Take(state, builder, signal, at);
        }

        Assert.Equal(100L, Assert.Single(Assert.Single(builder.Build(0, 1_000)!.Carousels).Versions).LastSeen);
    }

    [Fact]
    public void ARecordingEndsNoEarlierThanItStarts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DataBroadcastRecordBuilder().Build(10, 9));
    }

    [Fact(DisplayName = "BR-BD-005: a version put together again with other content takes the place of what was held")]
    public void AVersionPutTogetherAgainWithOtherContentTakesItsPlace()
    {
        DataBroadcastRecord record = Read(
            (Carousels.Carried(), 0),
            (Carousels.Listing(Carousels.Entry, (0, 1), (1, 1)), 0),
            (Carousels.Completed(Carousels.Entry, 0, 1), 100),
            (Carousels.Completed(Carousels.Entry, 1, 1), 150),
            (Carousels.Listing(Carousels.Entry, 1, [0], (0, 1), (1, 1)), 200),
            (Carousels.Completed(Carousels.Entry, 0, 1, "other.bml"), 300));

        Assert.Equal(
            [(1, 150L, "startup.bml"), (0, 300L, "other.bml")],
            Assert.Single(record.Carousels).Versions.Select(version => (version.ModuleId, version.FirstSeen, version.Resources[0].Path)));
    }

    [Fact(DisplayName = "BR-BD-005: a carousel whose download changes goes on as another carousel of the record")]
    public void ACarouselWhoseDownloadChangesGoesOnAsAnotherCarousel()
    {
        DataBroadcastRecord record = Read(
            (Carousels.Carried(), 0),
            (Carousels.Listing(Carousels.Entry, 1, [], (0, 1)), 0),
            (Carousels.Completed(Carousels.Entry, 0, 1), 100),
            (Carousels.Listing(Carousels.Entry, 2, [], (0, 1)), 200),
            (Carousels.Completed(Carousels.Entry, 0, 1), 300));

        Assert.Equal(
            [(Carousels.Entry, 1u, 100L), (Carousels.Entry, 2u, 300L)],
            record.Carousels.Select(carousel => (carousel.Tag, carousel.DownloadId, Assert.Single(carousel.Versions).FirstSeen)));
    }

    [Fact]
    public void AModuleThatArrivesBeforeTheProgrammeMapIsReadKeepsTheDownloadItCameIn()
    {
        DataBroadcastRecord record = Read(
            (Carousels.Listing(Carousels.Entry, 7, [], (0, 1)), 0),
            (Carousels.Completed(Carousels.Entry, 0, 1), 100),
            (Carousels.Carried(), 200));

        Assert.Equal(7u, Assert.Single(record.Carousels).DownloadId);
    }

    [Fact(DisplayName = "BR-BD-005: a recording with a carousel left out for being too large is marked incomplete")]
    public void ACarouselLeftOutMarksTheRecordIncomplete()
    {
        DataBroadcastRecord record = Read(
            (Carousels.Carried(), 0),
            (Carousels.Listing(Carousels.Entry, (0, 1)), 0),
            (new CarouselSignal.Dropped(Carousels.Other, CarouselDropReason.TooManyModules), 100));

        Assert.True(record.Incomplete);
    }

    [Fact]
    public void NoChangeAtAllIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => new DataBroadcastRecordBuilder().Take(null!, 0));
    }

    [Fact]
    public void NothingIsRecordedWhenNoCatalogWasEverRead()
    {
        DataBroadcastRecordBuilder builder = new();
        CarouselState state = new();

        foreach (CarouselDelta delta in state.Apply(new CarouselSignal.NotCarried(), 0))
        {
            builder.Take(delta, 0);
        }

        Assert.Null(builder.Build(0, 0));
    }

    [Theory(DisplayName = "BR-BD-005: the record says whether the broadcaster asks for its data broadcast to open by itself, as the last catalog read says")]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void TheRecordSaysWhetherItOpensByItselfAsTheLastCatalogSays(bool first, bool last)
    {
        DataBroadcastRecord record = Read(
            (Carousels.Carried(autoStart: first), 0),
            (Carousels.Listing(Carousels.Entry, (0, 1)), 0),
            (Carousels.Completed(Carousels.Entry, 0, 1), 100),
            (Carousels.Carried(autoStart: last), 200),
            (Carousels.Listing(Carousels.Entry, 1, [], (0, 1)), 300));

        Assert.Equal(last, record.AutoStart);
    }

    [Fact]
    public void TheRecordBeginsWhereItIsToldTo()
    {
        Assert.Equal(12_345L, ReadFrom(12_345, 12_345, (Carousels.Carried(), 12_345)).StartsAt);
    }

    [Fact(DisplayName = "BR-BD-005: a long history held within its size lets go of superseded versions as it goes, and builds the record leaving out everything else would have made")]
    public void ALongHistoryHeldWithinItsSizeLetsGoAsItGoes()
    {
        const long Most = 60_000;
        (CarouselSignal Signal, long At)[] history = [.. LongHistory()];
        CarouselState bounded = new();
        DataBroadcastRecordBuilder within = new(Most);
        long peak = 0;

        foreach ((CarouselSignal signal, long at) in history)
        {
            Take(bounded, within, signal, at);
            peak = Math.Max(peak, within.Bytes);
        }

        CarouselState unbounded = new();
        DataBroadcastRecordBuilder everything = new(long.MaxValue);

        foreach ((CarouselSignal signal, long at) in history)
        {
            Take(unbounded, everything, signal, at);
        }

        long ends = history[^1].At;
        DataBroadcastRecord whole = everything.Build(0, ends)!;
        DataBroadcastRecord kept = within.Build(0, ends)!;

        Assert.InRange(whole.Bytes, 10 * Most, long.MaxValue);
        Assert.InRange(peak, Most / 2, Most + RoundBytes);
        Assert.InRange(kept.Bytes, 0, Most);
        Assert.True(kept.Incomplete);
        Assert.Equal(Described(whole.Within(Most)), Described(kept));
    }

    private const int RoundBytes = 16 * 2_000;

    private static IEnumerable<(CarouselSignal Signal, long At)> LongHistory()
    {
        yield return (Carousels.Carried(), 0);

        for (int round = 0; round < 300; round++)
        {
            long at = (round + 1) * 1_000L;
            uint other = round < 150 ? 1u : 2u;
            int[] entryUpdated = [.. Enumerable.Range(0, 10).Where(module => round % EntryEvery(module) == 0)];
            int[] otherUpdated = [.. Enumerable.Range(0, 5).Where(module => round % (module + 2) == 0)];

            yield return (Carousels.Listing(Carousels.Entry, 1, entryUpdated, [.. Enumerable.Range(0, 10).Select(module => (module, VersionAt(round, EntryEvery(module))))]), at);
            yield return (Carousels.Listing(Carousels.Other, other, otherUpdated, [.. Enumerable.Range(0, 5).Select(module => (module, VersionAt(round, module + 2)))]), at);

            foreach (int module in entryUpdated)
            {
                yield return (Module(Carousels.Entry, module, VersionAt(round, EntryEvery(module))), at + module);
            }

            foreach (int module in otherUpdated)
            {
                yield return (Module(Carousels.Other, module, VersionAt(round, module + 2)), at + 20 + module);
            }

            yield return (new CarouselSignal.EventTimed(Carousels.Event(round, at + 50)), at + 50);
        }
    }

    private static int VersionAt(int round, int every) => (round / every) % 256;

    private static int EntryEvery(int module) => module is CarouselCatalog.StartupModuleId ? 100 : module + 1;

    private static CarouselSignal.ModuleCompleted Module(int tag, int module, int version)
        => new(tag, module, version, [Carousels.Resource("m.bml", 500 + (module * 97) + (version % 7))]);

    private static string Described(DataBroadcastRecord record)
        => string.Join(
            "\n",
            [
                $"{record.EntryTag} {record.Incomplete} {record.Bytes} {record.Events.Count}",
                .. record.Carousels.SelectMany(carousel => carousel.Versions.Select(version =>
                    $"{carousel.Tag} {carousel.DownloadId} {version.ModuleId} {version.Version} {version.FirstSeen} {version.LastSeen} {version.Bytes}")),
            ]);

    private static DataBroadcastRecord Read(params (CarouselSignal Signal, long At)[] signals)
        => ReadFrom(0, signals.Max(signal => signal.At), signals);

    private static DataBroadcastRecord ReadFrom(long startsAt, long endsAt, params (CarouselSignal Signal, long At)[] signals)
    {
        CarouselState state = new();
        DataBroadcastRecordBuilder builder = new();

        foreach ((CarouselSignal signal, long at) in signals)
        {
            Take(state, builder, signal, at);
        }

        return builder.Build(startsAt, endsAt) ?? throw new InvalidOperationException("A catalog was read.");
    }

    private static void Take(CarouselState state, DataBroadcastRecordBuilder builder, CarouselSignal signal, long at)
    {
        foreach (CarouselDelta delta in state.Apply(signal, at))
        {
            builder.Take(delta, at);
        }
    }
}
