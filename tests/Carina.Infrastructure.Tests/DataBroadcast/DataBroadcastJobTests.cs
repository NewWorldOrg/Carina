using System.Text;

using Carina.Domain.Captions;
using Carina.Domain.Channels;
using Carina.Domain.DataBroadcast;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.DataBroadcast;
using Carina.Infrastructure.Recordings;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.DataBroadcast;

public sealed class DataBroadcastJobTests : IDisposable
{
    private const int Entry = 0x40;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly OutputRoot Bulk = new("bulk");

    private static readonly OutputRoot Elsewhere = new("elsewhere");

    private static readonly ServiceId Service = new(1040);

    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly string recordings = Directory.CreateTempSubdirectory("carina-data-broadcast-recordings-").FullName;

    private readonly string shelved = Path.Combine(Directory.CreateTempSubdirectory("carina-data-broadcast-shelf-").FullName, "captions");

    private readonly HeldWorklist worklist = new();

    private readonly HeldTaker taker = new();

    private readonly HeldWatching watching = new();

    private readonly RecordingAppEvents events = new();

    private readonly RecordingReadTurn turn = new();

    private readonly HeldBusyness busyness;

    private DateTime? reservationStartsAt;

    public DataBroadcastJobTests()
    {
        busyness = new HeldBusyness(() => new Busyness(worklist.BeingRecorded, watching.Anyone, reservationStartsAt));
    }

    public void Dispose()
    {
        turn.Dispose();
        Directory.Delete(recordings, recursive: true);
        Directory.Delete(Path.GetDirectoryName(shelved)!, recursive: true);
    }

    [Fact(DisplayName = "BR-BS-001: the record taken is kept on the shelf and the recording says it is made with how many modules")]
    public async Task TheRecordTakenIsKeptAndTheRecordingSaysItIsMade()
    {
        DataBroadcastSubject subject = Recorded();
        taker.Answer = _ => DataBroadcastTaking.Taken(Record("<bml>1</bml>", 2));

        DataBroadcastPass pass = await Job().RunAsync(Cancel);

        Assert.Equal([(subject.Id, "Taken", 2)], worklist.Written);
        Assert.Equal(2, (await Shelf().ReadAsync(subject.Id, Cancel))!.Modules);
        Assert.Equal([(Path.Combine(recordings, subject.FileName.Value), Service)], taker.Asked);
        Assert.Equal((1, 1, 0, 0), (pass.Read, pass.Made, pass.Missing, pass.Failed));
        Assert.Equal(["recordings"], events.Signalled);
    }

    [Fact(DisplayName = "BR-BS-001: a recording with no data broadcast is missing, and a record kept before from its file goes")]
    public async Task ARecordingWithNoDataBroadcastIsMissing()
    {
        DataBroadcastSubject subject = Recorded();
        await Shelf().KeepAsync(subject.Id, Record("<bml>1</bml>"), Cancel);
        taker.Answer = _ => DataBroadcastTaking.Missing();

        DataBroadcastPass pass = await Job().RunAsync(Cancel);

        Assert.Equal([(subject.Id, "Taken", 0)], worklist.Written);
        Assert.False(Shelf().Holds(subject.Id));
        Assert.Equal(1, pass.Missing);
    }

    [Theory(DisplayName = "BR-BS-001: a record that could not be taken, or whose taking threw, is a failure and nothing is kept")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARecordThatCouldNotBeTakenIsAFailure(bool throws)
    {
        DataBroadcastSubject subject = Recorded();
        taker.Answer = _ => throws
            ? throw new InvalidDataException("the disk went away")
            : DataBroadcastTaking.Failed(DataBroadcastFault.ClockUnread, "it exited 1");

        DataBroadcastPass pass = await Job().RunAsync(Cancel);

        Assert.Equal([(subject.Id, "Failed", 0)], worklist.Written);
        Assert.False(Shelf().Holds(subject.Id));
        Assert.Equal(1, pass.Failed);
    }

    [Fact(DisplayName = "D-2: a recording whose file has gone keeps the record taken of it before, and is made with the modules it holds")]
    public async Task ARecordingWhoseFileHasGoneKeepsItsRecord()
    {
        DataBroadcastSubject subject = Recorded();
        await Shelf().KeepAsync(subject.Id, Record("<bml>1</bml>", 3), Cancel);
        File.Delete(Path.Combine(recordings, subject.FileName.Value));
        File.WriteAllBytes(Path.Combine(recordings, "another.m2ts"), new byte[16]);

        DataBroadcastPass pass = await Job().RunAsync(Cancel);

        Assert.Equal([(subject.Id, "Taken", 3)], worklist.Written);
        Assert.Equal("<bml>1</bml>", Body(await Shelf().ReadAsync(subject.Id, Cancel)));
        Assert.Empty(taker.Asked);
        Assert.Equal(1, pass.Made);
    }

    [Fact(DisplayName = "BR-BS-001: a recording whose file has gone and of which nothing was kept is missing, and nothing is read")]
    public async Task ARecordingWhoseFileHasGoneWithNothingKeptIsMissing()
    {
        File.WriteAllBytes(Path.Combine(recordings, "another.m2ts"), new byte[16]);
        DataBroadcastSubject gone = Subject();
        worklist.Awaiting.Add(gone);

        await Job().RunAsync(Cancel);

        Assert.Equal([(gone.Id, "Taken", 0)], worklist.Written);
        Assert.Empty(taker.Asked);
    }

    [Fact]
    public async Task AFileMissingFromARootThatHoldsNothingAtAllKeepsItsPlaceBecauseThatIsWhatALostMountLooksLike()
    {
        worklist.Awaiting.Add(Subject());

        DataBroadcastPass pass = await Job().RunAsync(Cancel);

        Assert.Empty(worklist.Written);
        Assert.Equal((1, 0, 1), (pass.Read, pass.Settled, pass.LeftForNextTime));
    }

    [Fact]
    public async Task ARecordingUnderARootThisProcessCannotFindKeepsItsPlace()
    {
        worklist.Awaiting.Add(Subject(Elsewhere));

        DataBroadcastPass pass = await Job().RunAsync(Cancel);

        Assert.Empty(worklist.Written);
        Assert.Equal(0, pass.Settled);
    }

    [Theory(DisplayName = "BR-BS-001: nothing is started while anything is being recorded or watched or a reservation starts within thirty minutes")]
    [InlineData("recording")]
    [InlineData("watching")]
    [InlineData("reservation")]
    public async Task NothingIsStartedWhileTheMachineIsBusy(string busy)
    {
        Recorded();
        worklist.FailedWithTriesLeft = 1;
        worklist.BeingRecorded = busy is "recording";
        watching.Anyone = busy is "watching";
        reservationStartsAt = busy is "reservation" ? Now.AddMinutes(30) : Now.AddMinutes(31);

        DataBroadcastPass pass = await Job().RunAsync(Cancel);

        Assert.True(pass.Yielded);
        Assert.Empty(taker.Asked);
        Assert.Empty(worklist.Written);
        Assert.Equal(1, worklist.FailedWithTriesLeft);
    }

    [Fact(DisplayName = "BR-BS-001: the machine is asked whether it is idle as of now, before anything is read")]
    public async Task TheMachineIsAskedWhetherItIsIdleAsOfNow()
    {
        await Job().RunAsync(Cancel);

        Assert.Equal([Now], busyness.Asked);
    }

    [Fact(DisplayName = "BR-BS-001: a pass stops before the next recording once somebody starts watching")]
    public async Task APassStopsBeforeTheNextRecordingOnceSomebodyStartsWatching()
    {
        Recorded();
        Recorded();
        taker.Answer = _ =>
        {
            watching.Anyone = true;

            return DataBroadcastTaking.Missing();
        };

        DataBroadcastPass pass = await Job().RunAsync(Cancel);

        Assert.Single(taker.Asked);
        Assert.Equal((2, 1, 1, true), (pass.Read, pass.Settled, pass.LeftForNextTime, pass.Yielded));
    }

    [Fact(DisplayName = "BR-BS-001: records that failed with tries left, and records said to be made that are not kept or are kept empty, are put back to be taken again")]
    public async Task FailedAndLostRecordsArePutBack()
    {
        RecordingId kept = RecordingId.New();
        RecordingId lost = RecordingId.New();
        RecordingId broken = RecordingId.New();
        await Shelf().KeepAsync(kept, Record("<bml>1</bml>"), Cancel);
        await File.WriteAllBytesAsync(Shelf().PathOf(broken)!, [], Cancel);
        worklist.Made.AddRange([kept, lost, broken]);
        worklist.FailedWithTriesLeft = 2;

        DataBroadcastPass pass = await Job().RunAsync(Cancel);

        Assert.Equal([lost, broken], worklist.Lost);
        Assert.Equal(0, worklist.FailedWithTriesLeft);
        Assert.Equal(4, pass.Requeued);
        Assert.Equal(["recordings"], events.Signalled);
    }

    [Fact(DisplayName = "BR-BS-001: recordings that ended with no record due are put to coming first, even when the machine is busy")]
    public async Task RecordingsThatEndedWithNoRecordDueArePutToComingFirst()
    {
        worklist.EndedNotYetDue = 2;
        watching.Anyone = true;

        DataBroadcastPass pass = await Job().RunAsync(Cancel);

        Assert.Equal(0, worklist.EndedNotYetDue);
        Assert.Equal((true, 2), (pass.Yielded, pass.Requeued));
        Assert.Equal(["recordings"], events.Signalled);
    }

    [Fact(DisplayName = "BR-BS-001: a recording that goes while its data broadcast is taken keeps nothing on the shelf")]
    public async Task ARecordingThatGoesKeepsNothing()
    {
        DataBroadcastSubject subject = Recorded();
        worklist.Gone.Add(subject.Id);
        taker.Answer = _ => DataBroadcastTaking.Taken(Record("<bml>1</bml>"));

        DataBroadcastPass pass = await Job().RunAsync(Cancel);

        Assert.False(Shelf().Holds(subject.Id));
        Assert.Equal(0, pass.Settled);
    }

    [Fact(DisplayName = "BR-BS-001: a recording is read in the turn shared with the captions, and waits while the captions hold it")]
    public async Task ARecordingIsReadInTheTurnSharedWithTheCaptions()
    {
        Recorded();
        bool heldMeanwhile = false;
        taker.Answer = _ =>
        {
            heldMeanwhile = turn.Held;

            return DataBroadcastTaking.Missing();
        };
        IDisposable captions = await turn.TakeAsync(Cancel);

        Task<DataBroadcastPass> pass = Job().RunAsync(Cancel);
        bool askedWhileCaptionsRead = !taker.Asked.IsEmpty;
        captions.Dispose();
        await pass;

        Assert.False(askedWhileCaptionsRead);
        Assert.True(heldMeanwhile);
        Assert.Single(taker.Asked);
        Assert.False(turn.Held);
    }

    [Fact(DisplayName = "BR-BS-001: somebody who starts watching while the pass waits for its turn stops it before anything is read, and the turn is given back")]
    public async Task SomebodyWhoStartsWatchingWhileThePassWaitsForItsTurnStopsIt()
    {
        DataBroadcastSubject subject = Recorded();
        IDisposable captions = await turn.TakeAsync(Cancel);

        Task<DataBroadcastPass> waiting = Job().RunAsync(Cancel);
        watching.Anyone = true;
        captions.Dispose();
        DataBroadcastPass pass = await waiting;

        Assert.True(pass.Yielded);
        Assert.Empty(taker.Asked);
        Assert.DoesNotContain(worklist.Written, written => written.Id == subject.Id);
        Assert.False(turn.Held);
    }

    [Fact]
    public async Task APassWithNowhereToKeepRecordsRefusesAndReadsNothing()
    {
        Recorded();

        DataBroadcastPass pass = await Job(new CaptionSettings()).RunAsync(Cancel);

        Assert.True(pass.NowhereToKeepThem);
        Assert.Empty(taker.Asked);
    }

    [Fact]
    public async Task APassAskedForWhileOneIsRunningIsRefused()
    {
        Recorded();
        DataBroadcastJob job = Job();
        TaskCompletionSource release = new();
        taker.Gate = release.Task;

        Task<DataBroadcastPass> first = job.RunAsync(Cancel);
        DataBroadcastPass second = await job.RunAsync(Cancel);
        release.SetResult();
        await first;

        Assert.True(second.AlreadyRunning);
    }

    [Fact(DisplayName = "BR-BS-001: the newest recordings are asked for, as many as a pass takes, under the roots within reach")]
    public async Task TheNewestRecordingsAreAskedForUnderTheRootsWithinReach()
    {
        await Job(new CaptionSettings { WrittenTo = shelved, AtMostAPass = 2 }).RunAsync(Cancel);

        Assert.Equal(2, worklist.AskedFor);
        Assert.Equal([Bulk], worklist.AskedWithin);
    }

    private CaptionSettings Settings() => new() { WrittenTo = shelved };

    private DataBroadcastShelf Shelf() => new(Settings());

    private DataBroadcastJob Job(CaptionSettings? settings = null)
    {
        CaptionSettings chosen = settings ?? Settings();

        return new DataBroadcastJob(
            Scopes(),
            taker,
            new DataBroadcastShelf(chosen),
            chosen,
            new IntegritySettings { OutputRoots = [new StorageRootPath(Bulk, recordings)] },
            turn,
            events,
            new HeldClock(Now),
            NullLogger<DataBroadcastJob>.Instance);
    }

    private IServiceScopeFactory Scopes()
    {
        ServiceCollection services = new();
        services.AddScoped<IDataBroadcastWorklist>(_ => worklist);
        services.AddScoped<IBusynessReader>(_ => busyness);

        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private DataBroadcastSubject Recorded()
    {
        DataBroadcastSubject subject = Subject();
        File.WriteAllBytes(Path.Combine(recordings, subject.FileName.Value), new byte[16]);
        worklist.Awaiting.Add(subject);

        return subject;
    }

    private static DataBroadcastSubject Subject(OutputRoot? root = null)
    {
        RecordingId id = RecordingId.New();

        return new DataBroadcastSubject(id, root ?? Bulk, RecordingFileName.For(id, ".m2ts"), Service);
    }

    private static DataBroadcastRecord Record(string startup, int modules = 1)
        => new(
            0,
            Entry,
            [
                new RecordedCarousel(
                    Entry,
                    1,
                    [
                        .. Enumerable.Range(0, modules).Select(module => new ModuleVersion(
                            Entry,
                            module,
                            1,
                            0,
                            0,
                            [new CarouselResource("startup.bml", "text/X-arib-bml", ResourceForm.Text, Encoding.UTF8.GetBytes(startup))])),
                    ]),
            ],
            [],
            false);

    private static string Body(DataBroadcastRecord? record)
        => Encoding.UTF8.GetString(record!.Carousels[0].Versions[0].Resources[0].Body.Span);

    private sealed class HeldClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private sealed class HeldWatching : IWatching
    {
        public bool Anyone { get; set; }
    }

    private sealed class HeldTaker : IDataBroadcastTaker
    {
        public Func<string, DataBroadcastTaking> Answer { get; set; } = _ => DataBroadcastTaking.Missing();

        public System.Collections.Concurrent.ConcurrentQueue<(string Source, ServiceId Service)> Asked { get; } = new();

        public Task? Gate { get; set; }

        public async Task<DataBroadcastTaking> TakeAsync(string source, ServiceId service, CancellationToken cancellationToken)
        {
            Asked.Enqueue((source, service));

            if (Gate is { } gate)
            {
                await gate;
            }

            return Answer(source);
        }
    }

    private sealed class HeldWorklist : IDataBroadcastWorklist
    {
        public List<DataBroadcastSubject> Awaiting { get; } = [];

        public List<(RecordingId Id, string Move, int Modules)> Written { get; } = [];

        public List<RecordingId> Made { get; } = [];

        public List<RecordingId> Lost { get; } = [];

        public HashSet<RecordingId> Gone { get; } = [];

        public bool BeingRecorded { get; set; }

        public int FailedWithTriesLeft { get; set; }

        public int EndedNotYetDue { get; set; }

        public int? AskedFor { get; private set; }

        public IReadOnlyList<OutputRoot> AskedWithin { get; private set; } = [];

        public Task<IReadOnlyList<DataBroadcastSubject>> AwaitingAsync(
            IReadOnlyList<OutputRoot> withinReach,
            int atMost,
            CancellationToken cancellationToken)
        {
            AskedFor = atMost;
            AskedWithin = withinReach;

            return Task.FromResult<IReadOnlyList<DataBroadcastSubject>>(
                [.. Awaiting.Where(subject => withinReach.Contains(subject.Root) && Written.All(written => written.Id != subject.Id)).Take(atMost)]);
        }

        public Task<int> WaitingOutOfReachAsync(IReadOnlyList<OutputRoot> withinReach, CancellationToken cancellationToken)
            => Task.FromResult(Awaiting.Count(subject => !withinReach.Contains(subject.Root)));

        public Task<IReadOnlyList<RecordingId>> MadeAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RecordingId>>([.. Made]);

        public Task<int> CatchUpEndedAsync(CancellationToken cancellationToken)
        {
            int caughtUp = EndedNotYetDue;
            EndedNotYetDue = 0;

            return Task.FromResult(caughtUp);
        }

        public Task<int> RetryFailedAsync(CancellationToken cancellationToken)
        {
            int retried = FailedWithTriesLeft;
            FailedWithTriesLeft = 0;

            return Task.FromResult(retried);
        }

        public Task<bool> LostAsync(RecordingId id, CancellationToken cancellationToken)
        {
            Lost.Add(id);

            return Task.FromResult(true);
        }

        public Task<bool> TakenAsync(RecordingId id, int modules, CancellationToken cancellationToken)
            => Write(id, "Taken", modules);

        public Task<bool> FailedAsync(RecordingId id, CancellationToken cancellationToken)
            => Write(id, "Failed", 0);

        private Task<bool> Write(RecordingId id, string move, int modules)
        {
            if (Gone.Contains(id))
            {
                return Task.FromResult(false);
            }

            Written.Add((id, move, modules));

            return Task.FromResult(true);
        }
    }
}
