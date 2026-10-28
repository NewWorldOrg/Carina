using System.IO.Pipelines;
using System.Threading.Channels;

using Carina.Domain.Auth;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class HlsKeysTests
{
    private const uint OneFrame = 1001;

    private static readonly Subject Watcher = new("a-watcher");

    private static readonly Subject Another = new("another-watcher");

    private readonly HandTurnedClock clock = new();

    [Fact(DisplayName = "BR-PV-005: a key is an unguessable value that opens what it was issued for")]
    public async Task AKeyIsAnUnguessableValueThatOpensWhatItWasIssuedFor()
    {
        await using HlsKeys keys = new(new HlsSettings(), clock);

        IssuedHlsKey issued = keys.Issue(new HeldSeat(), Watcher, LiveProfile.Hd30)!;

        Assert.True(Unguessable.IsOne(issued.Key));
        Assert.Same(issued.Rendition, keys.Find(issued.Key));
        Assert.Same(LiveProfile.Hd30, issued.Rendition.Profile);
    }

    [Fact]
    public async Task EveryKeyIssuedIsADifferentOne()
    {
        await using HlsKeys keys = new(new HlsSettings(), clock);

        IssuedHlsKey first = keys.Issue(new HeldSeat(), Watcher, LiveProfile.Hd30)!;
        IssuedHlsKey second = keys.Issue(new HeldSeat(), Watcher, LiveProfile.Hd30)!;

        Assert.NotEqual(first.Key, second.Key);
        Assert.NotSame(keys.Find(first.Key), keys.Find(second.Key));
    }

    [Theory(DisplayName = "BR-PV-005: a key never issued opens nothing")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task AKeyNeverIssuedOpensNothing(string? offered)
    {
        await using HlsKeys keys = new(new HlsSettings(), clock);

        keys.Issue(new HeldSeat(), Watcher, LiveProfile.Hd30);

        Assert.Null(keys.Find(offered));
    }

    [Fact]
    public void TheIssuedKeyIsNotWhatItSaysWhenWrittenOut()
    {
        string key = Unguessable.Issue();

        Assert.DoesNotContain(key, new IssuedHlsKey(key, new NoRendition()).ToString(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-PV-005: a key nothing has been asked with for thirty seconds is let go of, and the seat it held with it")]
    public async Task AKeyNothingHasBeenAskedWithForThirtySecondsIsLetGoOf()
    {
        await using HlsKeys keys = new(new HlsSettings(), clock);
        HeldSeat seat = new();
        IssuedHlsKey issued = keys.Issue(seat, Watcher, LiveProfile.Hd30)!;

        clock.Turn(TimeSpan.FromSeconds(29));

        Assert.NotNull(keys.Find(issued.Key));

        clock.Turn(TimeSpan.FromSeconds(30));

        Assert.Null(keys.Find(issued.Key));
        await Eventually.Happens(() => seat.LetGo, "the seat the key held was let go of");
        Assert.Equal(0, keys.Count);
    }

    [Fact(DisplayName = "BR-PV-005: a key goes on being held for as long as something keeps asking with it")]
    public async Task AKeyGoesOnBeingHeldForAsLongAsSomethingKeepsAskingWithIt()
    {
        await using HlsKeys keys = new(new HlsSettings(), clock);
        HeldSeat seat = new();
        IssuedHlsKey issued = keys.Issue(seat, Watcher, LiveProfile.Hd30)!;

        for (int asked = 0; asked < 10; asked++)
        {
            clock.Turn(TimeSpan.FromSeconds(20));

            Assert.NotNull(keys.Find(issued.Key));
        }

        Assert.False(seat.LetGo);
        Assert.Equal(1, keys.Count);
    }

    [Fact]
    public async Task AKeyLetGoOfWithoutBeingAskedForIsLetGoOfAtItsTimeAllTheSame()
    {
        await using HlsKeys keys = new(new HlsSettings(), clock);
        HeldSeat seat = new();

        keys.Issue(seat, Watcher, LiveProfile.Hd30);
        clock.Turn(TimeSpan.FromSeconds(30));

        await Eventually.Happens(() => seat.LetGo, "the seat the key held was let go of");
        Assert.Equal(0, keys.Count);
    }

    [Fact(DisplayName = "BR-PD-022: the first segment is waited for, and its coming is said")]
    public async Task TheFirstSegmentIsWaitedFor()
    {
        await using HlsKeys keys = new(new HlsSettings(), clock);
        HeldSeat seat = new();
        IHlsRendition rendition = keys.Issue(seat, Watcher, LiveProfile.Hd30)!.Rendition;
        Task<bool> waiting = rendition.ReadyAsync(CancellationToken.None);

        seat.Send(new LiveFrame(LiveChannel.PictureHeader, LivePts.Start, Fmp4Media.Initialisation()));
        seat.Send(Gop(0));

        Assert.False(waiting.IsCompleted);

        seat.Send(Gop(1));

        Assert.True(await waiting);
        Assert.Single(rendition.Window.Segments);
        Assert.NotNull(rendition.Segment(0));
        Assert.Equal(Fmp4Media.Initialisation(), rendition.Initialisation!.Value.ToArray());
    }

    [Fact]
    public async Task ASessionThatEndsBeforeTheFirstSegmentSaysThereIsNone()
    {
        await using HlsKeys keys = new(new HlsSettings(), clock);
        HeldSeat seat = new();
        IHlsRendition rendition = keys.Issue(seat, Watcher, LiveProfile.Hd30)!.Rendition;

        seat.NoMore();

        Assert.False(await rendition.ReadyAsync(CancellationToken.None));
        Assert.True(rendition.Window.Ended);
        await Eventually.Happens(() => seat.LetGo, "the seat of a session that ended was let go of");
    }

    [Fact]
    public async Task ASessionThatBreaksIsReadAsHavingEnded()
    {
        await using HlsKeys keys = new(new HlsSettings(), clock);
        HeldSeat seat = new();
        IHlsRendition rendition = keys.Issue(seat, Watcher, LiveProfile.Hd30)!.Rendition;

        seat.Breaks();

        Assert.False(await rendition.ReadyAsync(CancellationToken.None));
        Assert.True(rendition.Window.Ended);
    }

    [Fact]
    public async Task TheFirstSegmentIsWaitedForNoLongerThanTheWaitAllows()
    {
        await using HlsKeys keys = new(new HlsSettings { LongestWait = TimeSpan.FromSeconds(10) }, clock);
        IHlsRendition rendition = keys.Issue(new HeldSeat(), Watcher, LiveProfile.Hd30)!.Rendition;
        Task<bool> waiting = rendition.ReadyAsync(CancellationToken.None);

        clock.Turn(TimeSpan.FromSeconds(10));

        Assert.False(await waiting);
    }

    [Fact(DisplayName = "BR-PV-005: revoking what a watcher was let in with lets go of their keys and nobody else's")]
    public async Task RevokingWhatAWatcherWasLetInWithLetsGoOfTheirKeysAndNobodyElses()
    {
        await using HlsKeys keys = new(new HlsSettings(), clock);
        HeldSeat mine = new();
        HeldSeat theirs = new();
        IssuedHlsKey revoked = keys.Issue(mine, Watcher, LiveProfile.Hd30)!;
        IssuedHlsKey kept = keys.Issue(theirs, Another, LiveProfile.Hd30)!;

        Assert.Equal(1, keys.RevokeEverythingOf(Watcher));

        Assert.Null(keys.Find(revoked.Key));
        Assert.NotNull(keys.Find(kept.Key));
        await Eventually.Happens(() => mine.LetGo, "the revoked key's seat was let go of");
        Assert.False(theirs.LetGo);
    }

    [Fact]
    public async Task AKeyLetGoOfOpensNothingAndItsSeatIsLetGoOfAtOnce()
    {
        await using HlsKeys keys = new(new HlsSettings(), clock);
        HeldSeat seat = new();
        IssuedHlsKey issued = keys.Issue(seat, Watcher, LiveProfile.Hd30)!;

        keys.LetGo(issued.Key);
        keys.LetGo(issued.Key);
        keys.LetGo("short");

        Assert.Null(keys.Find(issued.Key));
        await Eventually.Happens(() => seat.LetGo, "the seat of the key let go of was let go of");
    }

    [Fact]
    public async Task NoMoreKeysAreIssuedThanMayBeHeldAtOnceAndTheSeatIsLeftToTheCaller()
    {
        await using HlsKeys keys = new(new HlsSettings { MostHeldAtOnce = 2 }, clock);
        HeldSeat refused = new();

        keys.Issue(new HeldSeat(), Watcher, LiveProfile.Hd30);
        keys.Issue(new HeldSeat(), Another, LiveProfile.Hd30);

        Assert.Null(keys.Issue(refused, Watcher, LiveProfile.Hd30));
        Assert.False(refused.LetGo);
        Assert.Equal(2, keys.Count);
    }

    [Fact]
    public async Task AWatcherHoldingAsManyKeysAsTheyMayGivesUpTheOneAskedWithLongestAgo()
    {
        await using HlsKeys keys = new(new HlsSettings { MostHeldPerWatcher = 2 }, clock);
        HeldSeat oldest = new();
        IssuedHlsKey first = keys.Issue(oldest, Watcher, LiveProfile.Hd30)!;

        clock.Turn(TimeSpan.FromSeconds(1));

        IssuedHlsKey second = keys.Issue(new HeldSeat(), Watcher, LiveProfile.Hd30)!;

        clock.Turn(TimeSpan.FromSeconds(1));
        keys.Find(first.Key);

        IssuedHlsKey third = keys.Issue(new HeldSeat(), Watcher, LiveProfile.Hd30)!;

        Assert.NotNull(keys.Find(first.Key));
        Assert.Null(keys.Find(second.Key));
        Assert.NotNull(keys.Find(third.Key));
        Assert.False(oldest.LetGo);
    }

    [Fact]
    public async Task LettingGoOfTheKeysLetsGoOfEverySeat()
    {
        HeldSeat first = new();
        HeldSeat second = new();
        HlsKeys keys = new(new HlsSettings(), clock);

        keys.Issue(first, Watcher, LiveProfile.Hd30);
        keys.Issue(second, Another, LiveProfile.Hd30);

        await keys.DisposeAsync();

        Assert.True(first.LetGo);
        Assert.True(second.LetGo);
        Assert.Null(keys.Issue(new HeldSeat(), Watcher, LiveProfile.Hd30));
    }

    [Fact(DisplayName = "BR-PD-022: a recording's key opens an event playlist cut from what its transcoder writes, in the profile it transcodes in")]
    public async Task ARecordingsKeyOpensAnEventPlaylistCutFromWhatItsTranscoderWrites()
    {
        await using HlsKeys keys = new(new HlsSettings(), clock);
        HeldTranscode transcode = new(LiveProfile.FullHd30);
        IssuedHlsKey issued = keys.Issue(transcode, Watcher)!;
        Task<bool> waiting = issued.Rendition.ReadyAsync(CancellationToken.None);

        await transcode.WriteAsync(Fmp4Media.Initialisation(), RecordedGop(0));

        Assert.False(waiting.IsCompleted);

        await transcode.WriteAsync(RecordedGop(1));

        Assert.True(await waiting);
        Assert.Same(issued.Rendition, keys.Find(issued.Key));
        Assert.Same(LiveProfile.FullHd30, issued.Rendition.Profile);
        Assert.Equal(HlsPlaylistType.Event, issued.Rendition.Window.Type);
        Assert.Equal(RecordedGop(0), issued.Rendition.Segment(0)!.Value.ToArray());
        Assert.Equal(Fmp4Media.Initialisation(), issued.Rendition.Initialisation!.Value.ToArray());
    }

    [Fact(DisplayName = "BR-PD-022: a recording is transcoded no further ahead of the furthest segment a player has asked for than the segments allowed")]
    public async Task ARecordingIsTranscodedNoFurtherAheadThanTheSegmentsAllowed()
    {
        await using HlsKeys keys = new(new HlsSettings { SegmentsAhead = 2 }, clock);
        HeldTranscode transcode = new(LiveProfile.Hd30);
        IHlsRendition rendition = keys.Issue(transcode, Watcher)!.Rendition;

        await transcode.WriteAsync([Fmp4Media.Initialisation(), .. Enumerable.Range(0, 10).Select(RecordedGop)]);

        await Eventually.Happens(() => rendition.Window.Segments.Count is 2, "two segments were cut");
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        Assert.Equal(2, rendition.Window.Segments.Count);

        rendition.Segment(0);

        await Eventually.Happens(() => rendition.Window.Segments.Count is 3, "a third segment was cut once the first was asked for");
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        Assert.Equal(3, rendition.Window.Segments.Count);

        rendition.Segment(2);

        await Eventually.Happens(() => rendition.Window.Segments.Count is 5, "two more were cut once the third was asked for");
    }

    [Fact(DisplayName = "BR-PD-022: asking for a recording's segment lets go of those further behind it than the segments held, which its playlist goes on listing")]
    public async Task AskingForARecordingsSegmentLetsGoOfThoseFurtherBehind()
    {
        await using HlsKeys keys = new(new HlsSettings { SegmentsHeld = 2, SegmentsAhead = 20 }, clock);
        HeldTranscode transcode = new(LiveProfile.Hd30);
        IHlsRendition rendition = keys.Issue(transcode, Watcher)!.Rendition;

        await transcode.WriteAsync([Fmp4Media.Initialisation(), .. Enumerable.Range(0, 8).Select(RecordedGop)]);
        await Eventually.Happens(() => rendition.Window.Segments.Count is 7, "seven segments were cut");

        Assert.NotNull(rendition.Segment(5));
        Assert.Null(rendition.Segment(2));
        Assert.NotNull(rendition.Segment(3));
        Assert.NotNull(rendition.Segment(4));
        Assert.NotNull(rendition.Segment(6));
        Assert.Equal(7, rendition.Window.Segments.Count);
    }

    [Fact(DisplayName = "BR-PD-022: a recording transcoded to its end ends its playlist with its last seconds in it and lets go of the transcoder, while its key goes on opening it")]
    public async Task ARecordingTranscodedToItsEndEndsItsPlaylistAndLetsGoOfTheTranscoder()
    {
        await using HlsKeys keys = new(new HlsSettings(), clock);
        HeldTranscode transcode = new(LiveProfile.Hd30);
        IssuedHlsKey issued = keys.Issue(transcode, Watcher)!;
        byte[] tail = Fmp4Media.Fragment(120 * OneFrame, [.. Enumerable.Repeat(OneFrame, 25)], true);

        await transcode.WriteAsync(Fmp4Media.Initialisation(), RecordedGop(0), RecordedGop(1), tail);
        transcode.Ends();

        await Eventually.Happens(() => issued.Rendition.Window.Ended, "the playlist ended");
        await Eventually.Happens(() => transcode.LetGo, "the transcoder was let go of");

        Assert.Equal(3, issued.Rendition.Window.Segments.Count);
        Assert.Equal(tail, issued.Rendition.Segment(2)!.Value.ToArray());
        Assert.NotNull(keys.Find(issued.Key));
    }

    [Fact(DisplayName = "BR-PV-005: a recording's key nothing has been asked with for thirty seconds lets go of the transcoder, even one waiting for the player to catch up")]
    public async Task ARecordingsKeyNothingHasBeenAskedWithLetsGoOfTheTranscoder()
    {
        await using HlsKeys keys = new(new HlsSettings { SegmentsAhead = 1 }, clock);
        HeldTranscode transcode = new(LiveProfile.Hd30);
        IssuedHlsKey issued = keys.Issue(transcode, Watcher)!;

        await transcode.WriteAsync([Fmp4Media.Initialisation(), .. Enumerable.Range(0, 6).Select(RecordedGop)]);
        await Eventually.Happens(() => issued.Rendition.Window.Segments.Count is 1, "a segment was cut");

        clock.Turn(TimeSpan.FromSeconds(30));

        await Eventually.Happens(() => transcode.LetGo, "the transcoder was let go of");
        Assert.Null(keys.Find(issued.Key));
        Assert.Equal(0, keys.Count);
    }

    [Fact]
    public async Task ATranscoderThatWritesSomethingOtherThanAnMp4EndsThePlaylistWithNothingInIt()
    {
        await using HlsKeys keys = new(new HlsSettings(), clock);
        HeldTranscode transcode = new(LiveProfile.Hd30);
        IHlsRendition rendition = keys.Issue(transcode, Watcher)!.Rendition;

        byte[] junk = [0, 0, 0, 16, .. "junk"u8.ToArray(), 1, 2, 3, 4, 5, 6, 7, 8];

        await transcode.WriteAsync(junk);

        Assert.False(await rendition.ReadyAsync(CancellationToken.None));
        Assert.True(rendition.Window.Ended);
        await Eventually.Happens(() => transcode.LetGo, "the transcoder was let go of");
    }

    [Fact(DisplayName = "BR-PV-005: the keys of recordings and of live channels are counted together, and a refused transcoder is left to the caller")]
    public async Task TheKeysOfRecordingsAndOfLiveChannelsAreCountedTogether()
    {
        await using HlsKeys keys = new(new HlsSettings { MostHeldAtOnce = 2 }, clock);
        HeldTranscode refused = new(LiveProfile.Hd30);

        keys.Issue(new HeldSeat(), Watcher, LiveProfile.Hd30);
        keys.Issue(new HeldTranscode(LiveProfile.Hd30), Another);

        Assert.Null(keys.Issue(refused, Watcher));
        Assert.False(refused.LetGo);
        Assert.Equal(2, keys.Count);
    }

    [Fact(DisplayName = "BR-PV-005: revoking what a watcher was let in with lets go of the transcoders of their recordings too")]
    public async Task RevokingWhatAWatcherWasLetInWithLetsGoOfTheirRecordingsToo()
    {
        await using HlsKeys keys = new(new HlsSettings(), clock);
        HeldTranscode transcode = new(LiveProfile.Hd30);
        IssuedHlsKey issued = keys.Issue(transcode, Watcher)!;

        Assert.Equal(1, keys.RevokeEverythingOf(Watcher));

        Assert.Null(keys.Find(issued.Key));
        await Eventually.Happens(() => transcode.LetGo, "the revoked key's transcoder was let go of");
    }

    private static byte[] RecordedGop(int number)
        => Fmp4Media.Fragment((ulong)number * 60 * OneFrame, [.. Enumerable.Repeat(OneFrame, 60)], true);

    private static LiveFrame Gop(int number)
        => new(
            LiveChannel.Picture,
            LivePts.Start,
            Fmp4Media.Fragment((ulong)number * 60 * OneFrame, [.. Enumerable.Repeat(OneFrame, 60)], true));

    private sealed class HeldSeat : ILiveViewing
    {
        private readonly Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        private int letGo;

        public ChannelReader<LiveFrame> Frames => frames.Reader;

        public LiveBacklog Backlog => LiveBacklog.Empty;

        public ILiveStartup? Startup => null;

        public ILiveEnding? Ending => null;

        public bool LetGo => Volatile.Read(ref letGo) is 1;

        public void Send(LiveFrame frame) => frames.Writer.TryWrite(frame);

        public void NoMore() => frames.Writer.TryComplete();

        public void Breaks() => frames.Writer.TryComplete(new InvalidOperationException("What was being sent live broke."));

        public ValueTask DisposeAsync()
        {
            Volatile.Write(ref letGo, 1);
            frames.Writer.TryComplete();

            return ValueTask.CompletedTask;
        }
    }

    private sealed class HeldTranscode(LiveProfile profile) : IOnTheFlyViewing
    {
        private readonly Pipe written = new();

        private int letGo;

        public OnTheFlyStanding Standing { get; } = new(
            TimeSpan.FromSeconds(90),
            TimeSpan.FromMilliseconds(100),
            profile,
            LiveEncoderChoice.Asked(LiveEncoder.Software),
            attributesWereMeasured: true,
            1,
            2);

        public Stream Output => written.Reader.AsStream();

        public Task<TranscoderExit> Completion { get; } = Task.FromResult(TranscoderExit.Finished());

        public bool LetGo => Volatile.Read(ref letGo) is 1;

        public async Task WriteAsync(params byte[][] pieces)
        {
            foreach (byte[] piece in pieces)
            {
                await written.Writer.WriteAsync(piece);
            }
        }

        public void Ends() => written.Writer.Complete();

        public ValueTask DisposeAsync()
        {
            Volatile.Write(ref letGo, 1);
            written.Writer.Complete();

            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoRendition : IHlsRendition
    {
        public LiveProfile Profile => LiveProfile.Hd30;

        public HlsWindow Window => HlsWindow.Empty;

        public ReadOnlyMemory<byte>? Initialisation => null;

        public ReadOnlyMemory<byte>? Segment(long number) => null;

        public Task<bool> ReadyAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
