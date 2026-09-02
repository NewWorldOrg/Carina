using System.Globalization;
using System.Runtime.Versioning;

using Carina.Domain.Channels;
using Carina.Domain.Integrity;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Playback;
using Carina.Infrastructure.Streaming;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Streaming;

[SupportedOSPlatform("linux")]
public sealed class OnTheFlyPlayerTests : IDisposable
{
    private const string TakesTheFileApart = """
        while [ "$#" -gt 0 ]; do
          case "$1" in
            -ss) ss=$2 ;;
            -i) src=$2 ;;
          esac
          shift
        done
        dd if="$src" bs=1000 skip="$ss" count=4 2>/dev/null
        """;

    private static readonly OutputRoot Root = new("bulk");

    private static readonly RecordingFileName Named = new("a1b2c3.ts");

    private static readonly ServiceId Service = new(1040);

    private readonly StandIns standIns = new();

    private TranscodeBudget budget = new(new TranscodeBudgetSettings { AtOnce = 2 });

    public void Dispose() => standIns.Dispose();

    [Fact]
    public async Task TwoStartingPositionsHandBackDifferentBytes()
    {
        Recorded(40_000);
        OnTheFlyPlayer player = Player(TakesTheFileApart, atOnce: 2);

        byte[] fromTheStart = await Watched(player, TimeSpan.Zero);
        byte[] fromLater = await Watched(player, TimeSpan.FromSeconds(20));

        Assert.NotEmpty(fromTheStart);
        Assert.NotEmpty(fromLater);
        Assert.NotEqual(fromTheStart, fromLater);
    }

    [Fact]
    public async Task TheCommandNamesTheRecordedServiceForItsPictureAndEveryOneOfItsSounds()
    {
        Recorded(40_000);
        string said = standIns.Named("arguments");

        await using IOnTheFlyViewing viewing = await Running(
            Player($"printf '%s\\n' \"$@\" > {said}; echo ready"),
            TimeSpan.Zero);

        string[] handed = File.ReadAllLines(said);

        Assert.Contains("p:1040:v:0", handed);
        Assert.Contains("p:1040:a", handed);
        Assert.Equal("copy", handed[Array.IndexOf(handed, "-c:a") + 1]);
    }

    [Fact]
    public async Task WhatIsReadBackHoldsTheBytesThatWereTastedBeforeTheStreamWasHandedOver()
    {
        Recorded(40_000);

        byte[] read = await Watched(Player(TakesTheFileApart), TimeSpan.FromSeconds(1));
        byte[] written = File.ReadAllBytes(Path.Combine(standIns.Room, Named.Value));

        Assert.Equal(written[1_000..5_000], read);
    }

    [Fact]
    public async Task WhatIsReadBackHoldsMoreThanTheFirstMouthfulWhenThereIsMoreThanThat()
    {
        Recorded(OnTheFlyPlayer.FirstChunk * 3);
        OnTheFlyPlayer player = Player("head -c 200000 /dev/zero | tr '\\0' 'x'");

        byte[] read = await Watched(player, TimeSpan.Zero);

        Assert.Equal(200_000, read.Length);
        Assert.All(read, letter => Assert.Equal((byte)'x', letter));
    }

    [Fact]
    public async Task TheBytesHandedBackCannotBeMovedAboutInPlace()
    {
        Recorded(40_000);

        await using IOnTheFlyViewing viewing = await Running(Player(TakesTheFileApart), TimeSpan.Zero);

        Assert.False(viewing.Output.CanSeek);
        Assert.False(viewing.Output.CanWrite);
        Assert.Throws<NotSupportedException>(() => viewing.Output.Seek(0, SeekOrigin.Begin));
    }

    [Fact]
    public async Task TheStandingSaysWhereInTheRecordingTheFirstByteSitsAndWhatGettingThereCost()
    {
        Recorded(40_000);

        await using IOnTheFlyViewing viewing = await Running(Player(TakesTheFileApart), TimeSpan.FromSeconds(12));

        Assert.Equal(TimeSpan.FromSeconds(12), viewing.Standing.StartsAt);
        Assert.True(viewing.Standing.Waited > TimeSpan.Zero);
        Assert.Equal(LiveProfile.Hd30, viewing.Standing.Profile);
        Assert.Equal(1, viewing.Standing.Running);
        Assert.Equal(2, viewing.Standing.AtOnce);
        Assert.True(viewing.Standing.AttributesWereMeasured);
    }

    [Fact]
    public async Task AViewerWhoGoesAwayLeavesNothingRunning()
    {
        Recorded(40_000);
        string pids = standIns.Named("pids");
        OnTheFlyPlayer player = Player($"echo $$ > {pids}; sleep 60 & echo $! >> {pids}; echo ready; wait");

        IOnTheFlyViewing viewing = await Running(player, TimeSpan.Zero);

        await WaitFor(pids, 2);
        await viewing.DisposeAsync();

        Assert.True(await standIns.NothingIsLeftOf(Read(pids)));
        Assert.Equal(0, budget.Running);
    }

    [Fact]
    public async Task WhenAsManyAreRunningAsTheMachineWillTheNextIsTurnedAwayRatherThanQuietlyTaken()
    {
        Recorded(40_000);
        OnTheFlyPlayer player = Player("echo ready; sleep 60", atOnce: 1);

        await using IOnTheFlyViewing first = await Running(player, TimeSpan.Zero);
        OnTheFlyStart second = await player.StartAsync(
            Found(),
            Service,
            TimeSpan.Zero,
            LiveProfile.Hd30,
            CancellationToken.None);

        Assert.False(second.Running);
        Assert.Equal(OnTheFlyRefusal.TooManyAlready, second.Refusal);
        Assert.Contains("1 transcoder", second.Note, StringComparison.Ordinal);
        Assert.Contains("(1)", second.Note, StringComparison.Ordinal);
        Assert.Equal(1, budget.Running);
    }

    [Fact]
    public async Task ALivePictureBeingSentTakesTheSamePlaceARecordingWould()
    {
        Recorded(40_000);
        OnTheFlyPlayer player = Player("echo ready; sleep 60", atOnce: 2);

        using ITranscodeSeat live = budget.Claim(TranscodePurpose.Live).Seat!;
        await using IOnTheFlyViewing first = await Running(player, TimeSpan.Zero);
        OnTheFlyStart third = await player.StartAsync(
            Found(),
            Service,
            TimeSpan.Zero,
            LiveProfile.Hd30,
            CancellationToken.None);

        Assert.Equal(2, first.Standing.Running);
        Assert.Equal(OnTheFlyRefusal.TooManyAlready, third.Refusal);
        Assert.Contains("2 transcoder", third.Note, StringComparison.Ordinal);
        Assert.Equal(2, budget.Running);
    }

    [Fact]
    public async Task ThePlaceAViewerGivesUpIsHandedToTheNextOne()
    {
        Recorded(40_000);
        OnTheFlyPlayer player = Player("echo ready; sleep 60", atOnce: 1);

        IOnTheFlyViewing first = await Running(player, TimeSpan.Zero);
        await first.DisposeAsync();

        await using IOnTheFlyViewing second = await Running(player, TimeSpan.Zero);

        Assert.Equal(1, budget.Running);
    }

    [Fact]
    public async Task TwoViewersOfTheSameRecordingAtTheSamePositionAreGivenATranscoderEach()
    {
        Recorded(40_000);
        OnTheFlyPlayer player = Player("echo ready; sleep 60", atOnce: 2);

        await using IOnTheFlyViewing first = await Running(player, TimeSpan.Zero);
        await using IOnTheFlyViewing second = await Running(player, TimeSpan.Zero);

        Assert.Equal(2, budget.Running);
        Assert.Equal(1, first.Standing.Running);
        Assert.Equal(2, second.Standing.Running);
        Assert.NotSame(first.Output, second.Output);
    }

    [Fact]
    public async Task ARecordingWithNoFileOnTheDiskIsRefusedWithoutStartingAnything()
    {
        OnTheFlyPlayer player = Player(TakesTheFileApart);

        OnTheFlyStart start = await player.StartAsync(
            new PlaybackFile(Root, Named, 40_000),
            Service,
            TimeSpan.Zero,
            LiveProfile.Hd30,
            CancellationToken.None);

        Assert.False(start.Running);
        Assert.Equal(OnTheFlyRefusal.NothingToPlay, start.Refusal);
        Assert.Equal(0, budget.Running);
    }

    [Fact]
    public async Task ARecordingWhoseFileHoldsNoBytesIsRefused()
    {
        Recorded(0);

        OnTheFlyStart start = await Player(TakesTheFileApart).StartAsync(
            new PlaybackFile(Root, Named, 0),
            Service,
            TimeSpan.Zero,
            LiveProfile.Hd30,
            CancellationToken.None);

        Assert.False(start.Running);
        Assert.Equal(OnTheFlyRefusal.NothingToPlay, start.Refusal);
    }

    [Fact]
    public async Task AFileThatWentAwayAfterThePlanWasMadeIsRefusedRatherThanStarted()
    {
        Recorded(40_000);
        PlaybackFile planned = Found();
        File.Delete(Path.Combine(standIns.Room, Named.Value));

        OnTheFlyStart start = await Player(TakesTheFileApart).StartAsync(
            planned,
            Service,
            TimeSpan.Zero,
            LiveProfile.Hd30,
            CancellationToken.None);

        Assert.False(start.Running);
        Assert.Equal(OnTheFlyRefusal.NothingToPlay, start.Refusal);
    }

    [Fact]
    public async Task ATranscoderThatWritesNoPictureIsRefusedWithWhatItComplainedOf()
    {
        Recorded(40_000);

        OnTheFlyStart start = await Player(
            "printf '%s\\n' 'Invalid data found when processing input' >&2; exit 183").StartAsync(
            Found(),
            Service,
            TimeSpan.Zero,
            LiveProfile.Hd30,
            CancellationToken.None);

        Assert.False(start.Running);
        Assert.Equal(OnTheFlyRefusal.NothingCameOut, start.Refusal);
        Assert.Contains("Invalid data found", start.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATranscoderThatEndsWithoutSayingAnythingIsStillRefusedInWordsSomebodyCanRead()
    {
        Recorded(40_000);

        OnTheFlyStart start = await Player("exit 0").StartAsync(
            Found(),
            Service,
            TimeSpan.Zero,
            LiveProfile.Hd30,
            CancellationToken.None);

        Assert.Equal(OnTheFlyRefusal.NothingCameOut, start.Refusal);
        Assert.NotEmpty(start.Note);
    }

    [Fact]
    public async Task AProgrammeThatIsNotOnThisMachineIsRefusedAndNamesNoPath()
    {
        Recorded(40_000);

        OnTheFlyStart start = await PlayerRunning(standIns.Named("no-such-programme")).StartAsync(
            Found(),
            Service,
            TimeSpan.Zero,
            LiveProfile.Hd30,
            CancellationToken.None);

        Assert.Equal(OnTheFlyRefusal.TranscoderWouldNotStart, start.Refusal);
        Assert.DoesNotContain('/', start.Note);
    }

    [Fact]
    public async Task ATranscoderThatNeverWritesAPictureIsGivenUpOnAndLeavesNothingRunning()
    {
        Recorded(40_000);
        string pids = standIns.Named("pids");
        OnTheFlyPlayer player = Player(
            $"echo $$ > {pids}; sleep 60 & echo $! >> {pids}; wait",
            waiting: TimeSpan.FromMilliseconds(300));

        OnTheFlyStart start = await player.StartAsync(
            Found(),
            Service,
            TimeSpan.Zero,
            LiveProfile.Hd30,
            CancellationToken.None);

        Assert.False(start.Running);
        Assert.Equal(OnTheFlyRefusal.TookTooLong, start.Refusal);
        Assert.Equal(0, budget.Running);
        Assert.True(await standIns.NothingIsLeftOf(Read(pids)));
    }

    [Fact]
    public async Task ARecordingIsPlayedFromSomewhereInItRatherThanFromBeforeItStarted()
    {
        Recorded(40_000);
        OnTheFlyPlayer player = Player(TakesTheFileApart);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => player.StartAsync(
                Found(),
                Service,
                TimeSpan.FromSeconds(-1),
                LiveProfile.Hd30,
                CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => player.StartAsync(null!, Service, TimeSpan.Zero, LiveProfile.Hd30, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => player.StartAsync(Found(), null!, TimeSpan.Zero, LiveProfile.Hd30, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => player.StartAsync(Found(), Service, TimeSpan.Zero, null!, CancellationToken.None));
    }

    private static async Task WaitFor(string pids, int howMany)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (File.Exists(pids) && Read(pids).Count() >= howMany)
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"the stand-in never wrote {howMany} process identifiers");
    }

    private static IEnumerable<int> Read(string pids)
    {
        try
        {
            return
            [
                .. File.ReadAllLines(pids)
                    .Where(line => line.Length > 0)
                    .Select(line => int.Parse(line, CultureInfo.InvariantCulture)),
            ];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static async Task<byte[]> Watched(OnTheFlyPlayer player, TimeSpan from)
    {
        await using IOnTheFlyViewing viewing = await Running(player, from);
        using var held = new MemoryStream();

        await viewing.Output.CopyToAsync(held, CancellationToken.None);

        return held.ToArray();
    }

    private static async Task<IOnTheFlyViewing> Running(OnTheFlyPlayer player, TimeSpan from)
    {
        OnTheFlyStart start = await player.StartAsync(
            new PlaybackFile(Root, Named, 40_000),
            Service,
            from,
            LiveProfile.Hd30,
            CancellationToken.None);

        Assert.True(start.Running, start.Note);

        return start.Viewing!;
    }

    private PlaybackFile Found() => Store().Find(Root, Named)!;

    private LocalPlaybackFileStore Store()
        => new(
            new IntegritySettings { OutputRoots = [new StorageRootPath(Root, standIns.Room)] },
            NullLogger<LocalPlaybackFileStore>.Instance);

    private void Recorded(int bytes)
        => File.WriteAllBytes(
            Path.Combine(standIns.Room, Named.Value),
            [.. Enumerable.Range(0, bytes).Select(at => (byte)((at * 7) % 251))]);

    private OnTheFlyPlayer Player(string body, int atOnce = 2, TimeSpan? waiting = null)
        => PlayerRunning(standIns.Script(body), atOnce, waiting);

    private OnTheFlyPlayer PlayerRunning(string programme, int atOnce = 2, TimeSpan? waiting = null)
    {
        budget = new TranscodeBudget(new TranscodeBudgetSettings { AtOnce = atOnce });

        return new OnTheFlyPlayer(
            new OnTheFlySettings { LongestWaitForTheFirstByte = waiting ?? TimeSpan.FromSeconds(10) },
            new LiveTranscodeSettings { Programme = programme, StopGrace = TimeSpan.FromMilliseconds(250) },
            budget,
            Store(),
            new Measured(),
            new AlreadyChosen(LiveEncoder.Software),
            TimeProvider.System);
    }

    private sealed class Measured : IStreamAttributeReader
    {
        public Task<StreamAttributeReading> ReadAsync(StreamSource source, CancellationToken cancellationToken)
            => Task.FromResult(StreamAttributeReading.Read(StreamAttributes.SafeSide, []));
    }

    private sealed class AlreadyChosen(LiveEncoder encoder) : ILiveEncoderSelector
    {
        public Task<LiveEncoderChoice> ChooseAsync(CancellationToken cancellationToken)
            => Task.FromResult(LiveEncoderChoice.Asked(encoder));
    }
}
