using System.Threading.Channels;

using Carina.Domain.Auth;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class LiveHlsKeysTests
{
    private const uint OneFrame = 1001;

    private static readonly Subject Watcher = new("a-watcher");

    private static readonly Subject Another = new("another-watcher");

    private readonly HandTurnedClock clock = new();

    [Fact(DisplayName = "BR-PV-005: a key is an unguessable value that opens what it was issued for")]
    public async Task AKeyIsAnUnguessableValueThatOpensWhatItWasIssuedFor()
    {
        await using LiveHlsKeys keys = new(new LiveHlsSettings(), clock);

        IssuedLiveHlsKey issued = keys.Issue(new HeldSeat(), Watcher, LiveProfile.Hd30)!;

        Assert.True(Unguessable.IsOne(issued.Key));
        Assert.Same(issued.Rendition, keys.Find(issued.Key));
        Assert.Same(LiveProfile.Hd30, issued.Rendition.Profile);
    }

    [Fact]
    public async Task EveryKeyIssuedIsADifferentOne()
    {
        await using LiveHlsKeys keys = new(new LiveHlsSettings(), clock);

        IssuedLiveHlsKey first = keys.Issue(new HeldSeat(), Watcher, LiveProfile.Hd30)!;
        IssuedLiveHlsKey second = keys.Issue(new HeldSeat(), Watcher, LiveProfile.Hd30)!;

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
        await using LiveHlsKeys keys = new(new LiveHlsSettings(), clock);

        keys.Issue(new HeldSeat(), Watcher, LiveProfile.Hd30);

        Assert.Null(keys.Find(offered));
    }

    [Fact]
    public void TheIssuedKeyIsNotWhatItSaysWhenWrittenOut()
    {
        string key = Unguessable.Issue();

        Assert.DoesNotContain(key, new IssuedLiveHlsKey(key, new NoRendition()).ToString(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-PV-005: a key nothing has been asked with for thirty seconds is let go of, and the seat it held with it")]
    public async Task AKeyNothingHasBeenAskedWithForThirtySecondsIsLetGoOf()
    {
        await using LiveHlsKeys keys = new(new LiveHlsSettings(), clock);
        HeldSeat seat = new();
        IssuedLiveHlsKey issued = keys.Issue(seat, Watcher, LiveProfile.Hd30)!;

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
        await using LiveHlsKeys keys = new(new LiveHlsSettings(), clock);
        HeldSeat seat = new();
        IssuedLiveHlsKey issued = keys.Issue(seat, Watcher, LiveProfile.Hd30)!;

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
        await using LiveHlsKeys keys = new(new LiveHlsSettings(), clock);
        HeldSeat seat = new();

        keys.Issue(seat, Watcher, LiveProfile.Hd30);
        clock.Turn(TimeSpan.FromSeconds(30));

        await Eventually.Happens(() => seat.LetGo, "the seat the key held was let go of");
        Assert.Equal(0, keys.Count);
    }

    [Fact(DisplayName = "BR-PD-022: the first segment is waited for, and its coming is said")]
    public async Task TheFirstSegmentIsWaitedFor()
    {
        await using LiveHlsKeys keys = new(new LiveHlsSettings(), clock);
        HeldSeat seat = new();
        ILiveHlsRendition rendition = keys.Issue(seat, Watcher, LiveProfile.Hd30)!.Rendition;
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
        await using LiveHlsKeys keys = new(new LiveHlsSettings(), clock);
        HeldSeat seat = new();
        ILiveHlsRendition rendition = keys.Issue(seat, Watcher, LiveProfile.Hd30)!.Rendition;

        seat.NoMore();

        Assert.False(await rendition.ReadyAsync(CancellationToken.None));
        Assert.True(rendition.Window.Ended);
        await Eventually.Happens(() => seat.LetGo, "the seat of a session that ended was let go of");
    }

    [Fact]
    public async Task ASessionThatBreaksIsReadAsHavingEnded()
    {
        await using LiveHlsKeys keys = new(new LiveHlsSettings(), clock);
        HeldSeat seat = new();
        ILiveHlsRendition rendition = keys.Issue(seat, Watcher, LiveProfile.Hd30)!.Rendition;

        seat.Breaks();

        Assert.False(await rendition.ReadyAsync(CancellationToken.None));
        Assert.True(rendition.Window.Ended);
    }

    [Fact]
    public async Task TheFirstSegmentIsWaitedForNoLongerThanTheWaitAllows()
    {
        await using LiveHlsKeys keys = new(new LiveHlsSettings { LongestWait = TimeSpan.FromSeconds(10) }, clock);
        ILiveHlsRendition rendition = keys.Issue(new HeldSeat(), Watcher, LiveProfile.Hd30)!.Rendition;
        Task<bool> waiting = rendition.ReadyAsync(CancellationToken.None);

        clock.Turn(TimeSpan.FromSeconds(10));

        Assert.False(await waiting);
    }

    [Fact(DisplayName = "BR-PV-005: revoking what a watcher was let in with lets go of their keys and nobody else's")]
    public async Task RevokingWhatAWatcherWasLetInWithLetsGoOfTheirKeysAndNobodyElses()
    {
        await using LiveHlsKeys keys = new(new LiveHlsSettings(), clock);
        HeldSeat mine = new();
        HeldSeat theirs = new();
        IssuedLiveHlsKey revoked = keys.Issue(mine, Watcher, LiveProfile.Hd30)!;
        IssuedLiveHlsKey kept = keys.Issue(theirs, Another, LiveProfile.Hd30)!;

        Assert.Equal(1, keys.RevokeEverythingOf(Watcher));

        Assert.Null(keys.Find(revoked.Key));
        Assert.NotNull(keys.Find(kept.Key));
        await Eventually.Happens(() => mine.LetGo, "the revoked key's seat was let go of");
        Assert.False(theirs.LetGo);
    }

    [Fact]
    public async Task NoMoreKeysAreIssuedThanMayBeHeldAtOnceAndTheSeatIsLeftToTheCaller()
    {
        await using LiveHlsKeys keys = new(new LiveHlsSettings { MostHeldAtOnce = 2 }, clock);
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
        await using LiveHlsKeys keys = new(new LiveHlsSettings { MostHeldPerWatcher = 2 }, clock);
        HeldSeat oldest = new();
        IssuedLiveHlsKey first = keys.Issue(oldest, Watcher, LiveProfile.Hd30)!;

        clock.Turn(TimeSpan.FromSeconds(1));

        IssuedLiveHlsKey second = keys.Issue(new HeldSeat(), Watcher, LiveProfile.Hd30)!;

        clock.Turn(TimeSpan.FromSeconds(1));
        keys.Find(first.Key);

        IssuedLiveHlsKey third = keys.Issue(new HeldSeat(), Watcher, LiveProfile.Hd30)!;

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
        LiveHlsKeys keys = new(new LiveHlsSettings(), clock);

        keys.Issue(first, Watcher, LiveProfile.Hd30);
        keys.Issue(second, Another, LiveProfile.Hd30);

        await keys.DisposeAsync();

        Assert.True(first.LetGo);
        Assert.True(second.LetGo);
        Assert.Null(keys.Issue(new HeldSeat(), Watcher, LiveProfile.Hd30));
    }

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

    private sealed class NoRendition : ILiveHlsRendition
    {
        public LiveProfile Profile => LiveProfile.Hd30;

        public LiveHlsWindow Window => LiveHlsWindow.Empty;

        public ReadOnlyMemory<byte>? Initialisation => null;

        public ReadOnlyMemory<byte>? Segment(long number) => null;

        public Task<bool> ReadyAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
