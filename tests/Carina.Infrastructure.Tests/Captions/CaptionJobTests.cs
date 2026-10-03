using System.Collections.Concurrent;

using Carina.Domain.Captions;
using Carina.Domain.Channels;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Captions;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Captions;

public sealed class CaptionJobTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly OutputRoot Bulk = new("bulk");

    private static readonly OutputRoot Elsewhere = new("elsewhere");

    private static readonly ServiceId Service = new(1040);

    private readonly string recordings = Directory.CreateTempSubdirectory("carina-caption-recordings-").FullName;

    private readonly string shelved = Path.Combine(Directory.CreateTempSubdirectory("carina-caption-shelf-").FullName, "captions");

    private readonly HeldCaptionWorklist worklist = new();

    private readonly HeldTranscriber transcriber = new();

    private readonly HeldWatching watching = new();

    private readonly RecordingAppEvents events = new();

    public void Dispose()
    {
        Directory.Delete(recordings, recursive: true);
        Directory.Delete(Path.GetDirectoryName(shelved)!, recursive: true);
    }

    [Fact]
    public async Task BrPd016TheCaptionsTakenAreKeptOnTheShelfAndTheRecordingSaysHowManyChangesThereAre()
    {
        CaptionSubject subject = Recorded();
        transcriber.Answer = _ => CaptionTranscription.Transcribed(Record(3));

        CaptionPass pass = await Job().RunAsync(Cancel);

        Assert.Equal([(subject.Id, CaptionState.Ready, (int?)3)], worklist.Written);
        Assert.Equal(3, (await new CaptionShelf(Settings()).ReadAsync(subject.Id, Cancel))!.Cues.Count);
        Assert.Equal([(Path.Combine(recordings, subject.FileName.Value), Service)], transcriber.Asked);
        Assert.Equal((1, 1, 0, 0), (pass.Read, pass.Kept, pass.Absent, pass.Failed));
        Assert.Equal(["recordings"], events.Signalled);
    }

    [Fact]
    public async Task BrPd016AServiceWithNoCaptionStreamHasNoCaptionsAndAnythingKeptBeforeGoes()
    {
        CaptionSubject subject = Recorded();
        await new CaptionShelf(Settings()).KeepAsync(subject.Id, Record(1), Cancel);
        transcriber.Answer = _ => CaptionTranscription.WithoutACaptionStream("matches no streams");

        CaptionPass pass = await Job().RunAsync(Cancel);

        Assert.Equal([(subject.Id, CaptionState.Absent, (int?)null)], worklist.Written);
        Assert.False(new CaptionShelf(Settings()).Holds(subject.Id));
        Assert.Equal(1, pass.Absent);
    }

    [Fact]
    public async Task BrPd016ARecordingWhoseCaptionsShowedNothingHasNoCaptions()
    {
        CaptionSubject subject = Recorded();
        transcriber.Answer = _ => CaptionTranscription.NothingShown();

        await Job().RunAsync(Cancel);

        Assert.Equal([(subject.Id, CaptionState.Absent, (int?)null)], worklist.Written);
    }

    [Fact]
    public async Task BrPd016ARecordingWhoseFileIsNotOnTheDiskHasNoCaptionsAndNothingIsRun()
    {
        Recorded();
        CaptionSubject gone = Subject();
        worklist.Awaiting.Add(gone);
        transcriber.Answer = _ => CaptionTranscription.Transcribed(Record(1));

        CaptionPass pass = await Job().RunAsync(Cancel);

        Assert.Contains((gone.Id, CaptionState.Absent, (int?)null), worklist.Written);
        Assert.DoesNotContain(transcriber.Asked, asked => asked.Source.Contains(gone.Id.Wire, StringComparison.Ordinal));
        Assert.Equal(1, pass.Absent);
    }

    [Fact]
    public async Task AFileMissingFromARootThatHoldsNothingAtAllKeepsItsPlaceBecauseThatIsWhatALostMountLooksLike()
    {
        CaptionSubject gone = Subject();
        worklist.Awaiting.Add(gone);

        CaptionPass pass = await Job().RunAsync(Cancel);

        Assert.Empty(worklist.Written);
        Assert.Equal((1, 0, 1), (pass.Read, pass.Settled, pass.LeftForNextTime));
        Assert.Empty(events.Signalled);
    }

    [Theory]
    [InlineData(CaptionFault.Refused)]
    [InlineData(CaptionFault.TimedOut)]
    [InlineData(CaptionFault.ClockUnread)]
    public async Task BrPd016ATranscriptionThatFailedIsCountedAsAFailureAndNothingIsKept(CaptionFault fault)
    {
        CaptionSubject subject = Recorded();
        transcriber.Answer = _ => CaptionTranscription.Failed(fault, "it fell over");

        CaptionPass pass = await Job().RunAsync(Cancel);

        Assert.Equal([(subject.Id, CaptionState.Failed, (int?)null)], worklist.Written);
        Assert.False(Directory.Exists(shelved));
        Assert.Equal(1, pass.Failed);
    }

    [Fact]
    public async Task BrPd016ARecordThatCannotBeWrittenIsAFailure()
    {
        CaptionSubject subject = Recorded();
        Directory.CreateDirectory(Path.Combine(shelved, subject.Id.Wire + CaptionSettings.Extension, "in-the-way"));
        transcriber.Answer = _ => CaptionTranscription.Transcribed(Record(2));

        CaptionPass pass = await Job().RunAsync(Cancel);

        Assert.Equal([(subject.Id, CaptionState.Failed, (int?)null)], worklist.Written);
        Assert.Equal(1, pass.Failed);
    }

    [Fact]
    public async Task BrPd016NothingIsStartedWhileSomebodyIsWatching()
    {
        Recorded();
        watching.Anyone = true;

        CaptionPass pass = await Job().RunAsync(Cancel);

        Assert.Empty(transcriber.Asked);
        Assert.True(pass.Yielded);
        Assert.Equal(1, pass.LeftForNextTime);
    }

    [Fact]
    public async Task BrPd016NothingIsStartedWhileSomethingIsBeingRecorded()
    {
        Recorded();
        worklist.BeingRecorded = true;

        CaptionPass pass = await Job().RunAsync(Cancel);

        Assert.Empty(transcriber.Asked);
        Assert.True(pass.Yielded);
    }

    [Fact]
    public async Task BrPd016APassAsksAgainBeforeEachRecordingAndStopsOnceSomebodyStartsWatching()
    {
        Recorded();
        Recorded();
        transcriber.Answer = _ =>
        {
            watching.Anyone = true;

            return CaptionTranscription.NothingShown();
        };

        CaptionPass pass = await Job().RunAsync(Cancel);

        Assert.Single(transcriber.Asked);
        Assert.Equal((2, 1, 1), (pass.Read, pass.Settled, pass.LeftForNextTime));
        Assert.True(pass.Yielded);
    }

    [Fact]
    public async Task APassTakesNoMoreThanItIsAllowedAndOnlyWhatIsWithinReach()
    {
        worklist.Awaiting.Add(Subject(Elsewhere));
        Recorded();

        await Job(Settings() with { AtMostAPass = 2 }).RunAsync(Cancel);

        Assert.Equal(2, worklist.AskedFor);
        Assert.Equal([Bulk], worklist.AskedWithin);
    }

    [Fact]
    public async Task ARecordingUnderARootThisProcessCannotFindKeepsItsPlace()
    {
        worklist.Awaiting.Add(Subject(Elsewhere));

        CaptionPass pass = await new CaptionJob(
            Scopes(),
            transcriber,
            new CaptionShelf(Settings()),
            Settings(),
            new IntegritySettings { OutputRoots = [new StorageRootPath(Bulk, recordings), new StorageRootPath(Elsewhere, recordings)] },
            watching,
            events,
            TimeProvider.System,
            NullLogger<CaptionJob>.Instance).RunAsync(Cancel);

        Assert.Equal(1, pass.Read);
    }

    [Fact]
    public async Task WithNowhereToKeepThemNoPassIsRun()
    {
        Recorded();

        CaptionPass pass = await Job(new CaptionSettings()).RunAsync(Cancel);

        Assert.True(pass.NowhereToKeepThem);
        Assert.Equal(0, worklist.Reads);
    }

    [Fact]
    public async Task OnlyOnePassRunsAtATime()
    {
        Recorded();
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        worklist.Gate = gate;
        CaptionJob job = Job();

        Task<CaptionPass> first = job.RunAsync(Cancel);
        CaptionPass second = await job.RunAsync(Cancel);
        gate.SetResult();
        await first;

        Assert.True(second.AlreadyRunning);
        Assert.Equal(1, worklist.Reads);
    }

    [Fact]
    public async Task ATranscriberThatThrowsLeavesTheRecordingWhereItWasAndTheRestOfThePassGoesOn()
    {
        Recorded();
        CaptionSubject second = Recorded();
        int asked = 0;
        transcriber.Answer = _ => Interlocked.Increment(ref asked) is 1
            ? throw new InvalidOperationException("the transcriber fell over")
            : CaptionTranscription.NothingShown();

        CaptionPass pass = await Job().RunAsync(Cancel);

        Assert.Equal([(second.Id, CaptionState.Absent, (int?)null)], worklist.Written);
        Assert.Equal((2, 1), (pass.Read, pass.Settled));
    }

    private CaptionSettings Settings() => new() { WrittenTo = shelved };

    private CaptionJob Job(CaptionSettings? settings = null)
    {
        CaptionSettings chosen = settings ?? Settings();

        return new CaptionJob(
            Scopes(),
            transcriber,
            new CaptionShelf(chosen),
            chosen,
            new IntegritySettings { OutputRoots = [new StorageRootPath(Bulk, recordings)] },
            watching,
            events,
            TimeProvider.System,
            NullLogger<CaptionJob>.Instance);
    }

    private IServiceScopeFactory Scopes()
    {
        ServiceCollection services = new();
        services.AddScoped<ICaptionWorklist>(_ => worklist);

        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private CaptionSubject Recorded()
    {
        CaptionSubject subject = Subject();
        File.WriteAllBytes(Path.Combine(recordings, subject.FileName.Value), new byte[16]);
        worklist.Awaiting.Add(subject);

        return subject;
    }

    private static CaptionSubject Subject(OutputRoot? root = null)
    {
        RecordingId id = RecordingId.New();

        return new CaptionSubject(id, root ?? Bulk, RecordingFileName.For(id, ".ts"), Service);
    }

    private static CaptionRecord Record(int changes)
        => new(
            1440,
            1080,
            TimeSpan.FromSeconds(2),
            [.. Enumerable.Range(0, changes).Select(at => new CaptionCue(90_000L * at, new CaptionPlacement(0, 0, 1, 1, new byte[] { 1 })))]);

    private sealed class HeldCaptionWorklist : ICaptionWorklist
    {
        public List<CaptionSubject> Awaiting { get; } = [];

        public List<(RecordingId Id, CaptionState State, int? Pictures)> Written { get; } = [];

        public bool BeingRecorded { get; set; }

        public int Reads { get; private set; }

        public int? AskedFor { get; private set; }

        public IReadOnlyList<OutputRoot> AskedWithin { get; private set; } = [];

        public TaskCompletionSource? Gate { get; set; }

        public async Task<IReadOnlyList<CaptionSubject>> AwaitingAsync(
            IReadOnlyList<OutputRoot> withinReach,
            int atMost,
            CancellationToken cancellationToken)
        {
            Reads++;
            AskedFor = atMost;
            AskedWithin = withinReach;

            if (Gate is { } waiting)
            {
                await waiting.Task.WaitAsync(cancellationToken);
            }

            return [.. Awaiting.Where(subject => withinReach.Contains(subject.Root)).Take(atMost)];
        }

        public Task<int> WaitingOutOfReachAsync(IReadOnlyList<OutputRoot> withinReach, CancellationToken cancellationToken)
            => Task.FromResult(Awaiting.Count(subject => !withinReach.Contains(subject.Root)));

        public Task<bool> AnyBeingRecordedAsync(CancellationToken cancellationToken) => Task.FromResult(BeingRecorded);

        public Task CaptionAsync(RecordingId id, CaptionState state, int? pictures, CancellationToken cancellationToken)
        {
            Written.Add((id, state, pictures));

            return Task.CompletedTask;
        }
    }

    private sealed class HeldTranscriber : ICaptionTranscriber
    {
        public Func<string, CaptionTranscription> Answer { get; set; } = _ => CaptionTranscription.NothingShown();

        public ConcurrentQueue<(string Source, ServiceId Service)> Asked { get; } = new();

        public Task<CaptionTranscription> TranscribeAsync(string source, ServiceId service, CancellationToken cancellationToken)
        {
            Asked.Enqueue((source, service));

            return Task.FromResult(Answer(source));
        }
    }

    private sealed class HeldWatching : IWatching
    {
        public bool Anyone { get; set; }
    }
}
