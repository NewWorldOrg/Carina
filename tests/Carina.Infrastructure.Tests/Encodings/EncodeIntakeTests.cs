using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Encodings;
using Carina.TestSupport;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class EncodeIntakeTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Began = new(2026, 9, 4, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Now = new(2026, 9, 4, 5, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "BR-ED2-004: a recording that has ended is put in the queue without anyone asking")]
    public async Task ARecordingThatHasEndedIsPutInTheQueueWithoutAnyoneAsking()
    {
        var machine = new Machine();
        Recording ended = machine.Recorded(RecordingOutcome.Complete);

        EncodeIntake took = await machine.Round().TakeAsync(1, Cancel);

        Assert.Equal(1, took.Queued);
        Assert.Equal(EncodeUnaskedStanding.Settled, took.Standing);
        EncodeJob queued = Assert.Single(machine.Jobs.Jobs);
        Assert.Equal(ended.Id, queued.RecordingId);
        Assert.Equal(machine.Profile.Id, queued.ProfileId);
        Assert.Equal(machine.Destination.Id, queued.DestinationId);
        Assert.Equal(machine.Destination.OutputRoot, queued.OutputRoot);
        Assert.Equal(EncodeJobStatus.Queued, queued.Status);
        Assert.Equal(Now, queued.QueuedAt);
    }

    [Fact(DisplayName = "BR-ED2-004: a recording that failed has nothing to encode and is never queued")]
    public async Task ARecordingThatFailedIsNeverQueued()
    {
        var machine = new Machine();
        machine.Recorded(RecordingOutcome.Failed);

        EncodeIntake took = await machine.Round().TakeAsync(1, Cancel);

        Assert.Equal(0, took.Queued);
        Assert.Empty(machine.Jobs.Jobs);
    }

    [Fact(DisplayName = "BR-ED2-004: a recording cut short is queued like any other, and what says it was cut short is the recording")]
    public async Task ARecordingCutShortIsQueuedLikeAnyOther()
    {
        var machine = new Machine();
        Recording cutShort = machine.Recorded(RecordingOutcome.Truncated);

        EncodeIntake took = await machine.Round().TakeAsync(1, Cancel);

        Assert.Equal(1, took.Queued);
        Assert.Equal(cutShort.Id, Assert.Single(machine.Jobs.Jobs).RecordingId);
        Assert.Equal(RecordingOutcome.Truncated, cutShort.Outcome);
    }

    [Fact(DisplayName = "BR-ED2-004: a recording still being written is not queued until it has ended")]
    public async Task ARecordingStillBeingWrittenIsNotQueued()
    {
        var machine = new Machine();
        machine.StillWriting();

        EncodeIntake took = await machine.Round().TakeAsync(1, Cancel);

        Assert.Equal(0, took.Queued);
        Assert.Empty(machine.Jobs.Jobs);
    }

    [Theory(DisplayName = "BR-ED2-004: a recording the ledger already holds a job for is not queued a second time, whatever became of that job")]
    [InlineData(EncodeJobStatus.Queued)]
    [InlineData(EncodeJobStatus.Running)]
    [InlineData(EncodeJobStatus.Completed)]
    [InlineData(EncodeJobStatus.Failed)]
    [InlineData(EncodeJobStatus.Cancelled)]
    public async Task ARecordingThatAlreadyHasAJobIsNotQueuedASecondTime(EncodeJobStatus status)
    {
        var machine = new Machine();
        Recording ended = machine.Recorded(RecordingOutcome.Complete);
        machine.Jobs.Jobs.Add(Job(ended.Id, machine.Profile.Id, machine.Destination, status));

        EncodeIntake took = await machine.Round().TakeAsync(1, Cancel);

        Assert.Equal(0, took.Queued);
        Assert.Single(machine.Jobs.Jobs);
    }

    [Fact(DisplayName = "BR-ED2-004: a second look over a queue this one filled adds nothing")]
    public async Task ASecondLookOverAQueueThisOneFilledAddsNothing()
    {
        var machine = new Machine();
        machine.Recorded(RecordingOutcome.Complete);
        machine.Recorded(RecordingOutcome.Complete);

        EncodeIntake first = await machine.Round().TakeAsync(1, Cancel);
        EncodeIntake second = await machine.Round().TakeAsync(1, Cancel);

        Assert.Equal(2, first.Queued);
        Assert.Equal(0, second.Queued);
        Assert.Equal(2, machine.Jobs.Jobs.Count);
    }

    [Fact(DisplayName = "BR-ED2-004: a machine that cannot settle where an artefact goes queues nothing and says what is missing")]
    public async Task AMachineThatCannotSettleWhereAnArtefactGoesQueuesNothing()
    {
        var machine = new Machine();
        machine.Recorded(RecordingOutcome.Complete);
        machine.Destinations.Destinations.Clear();

        EncodeIntake took = await machine.Round().TakeAsync(1, Cancel);

        Assert.Equal(0, took.Queued);
        Assert.Equal(EncodeUnaskedStanding.NothingIsDefined, took.Standing);
        Assert.Empty(machine.Jobs.Jobs);
    }

    [Fact(DisplayName = "BR-ED2-004: a machine offering more than one destination queues nothing and says so")]
    public async Task AMachineOfferingMoreThanOneDestinationQueuesNothing()
    {
        var machine = new Machine();
        machine.Recorded(RecordingOutcome.Complete);
        machine.Destinations.Destinations.Add(EncodeDestination.Define(
            new EncodeDestinationId(Guid.NewGuid()),
            new EncodeLabel("Another shelf"),
            new OutputRoot("elsewhere"),
            machine.Profile.Id,
            Began));

        EncodeIntake took = await machine.Round().TakeAsync(1, Cancel);

        Assert.Equal(0, took.Queued);
        Assert.Equal(EncodeUnaskedStanding.MoreThanOneIsOffered, took.Standing);
        Assert.Empty(machine.Jobs.Jobs);
    }

    [Fact(DisplayName = "BR-ED2-004: a machine that could not settle a destination queues what it passed over once it can")]
    public async Task AMachineThatCouldNotSettleADestinationQueuesWhatItPassedOverOnceItCan()
    {
        var machine = new Machine();
        machine.Recorded(RecordingOutcome.Complete);
        EncodeDestination shelf = machine.Destination;
        machine.Destinations.Destinations.Clear();

        EncodeIntake passed = await machine.Round().TakeAsync(1, Cancel);
        machine.Destinations.Destinations.Add(shelf);
        EncodeIntake took = await machine.Round().TakeAsync(1, Cancel);

        Assert.Equal(0, passed.Queued);
        Assert.Equal(1, took.Queued);
    }

    [Fact(DisplayName = "BR-ED2-004: more recordings than one look reads are queued a page at a time, and every one of them ends up in the queue")]
    public async Task MoreRecordingsThanOneLookReadsAreQueuedAPageAtATime()
    {
        var machine = new Machine();

        for (int made = 0; made < EncodeIntakeRound.PerLook + 7; made++)
        {
            machine.Recorded(RecordingOutcome.Complete, Began.AddMinutes(made));
        }

        EncodeIntake first = await machine.Round().TakeAsync(1, Cancel);
        EncodeIntake second = await machine.Round().TakeAsync(first.Page + 1, Cancel);

        Assert.True(first.MorePages);
        Assert.Equal(EncodeIntakeRound.PerLook, first.Queued);
        Assert.False(second.MorePages);
        Assert.Equal(7, second.Queued);
        Assert.Equal(EncodeIntakeRound.PerLook + 7, machine.Jobs.Jobs.Count);
        Assert.Equal(machine.Jobs.Jobs.Count, machine.Jobs.Jobs.Select(job => job.RecordingId).Distinct().Count());
    }

    [Fact(DisplayName = "A look that queued something tells the screens the jobs moved")]
    public async Task ALookThatQueuedSomethingTellsTheScreensTheJobsMoved()
    {
        var machine = new Machine();
        machine.Recorded(RecordingOutcome.Complete);

        await machine.Round().TakeAsync(1, Cancel);

        Assert.Equal([AppEventName.EncodeJobs], machine.Events.Signalled);
    }

    [Fact(DisplayName = "A look that queued nothing tells the screens nothing")]
    public async Task ALookThatQueuedNothingTellsTheScreensNothing()
    {
        var machine = new Machine();
        machine.Recorded(RecordingOutcome.Failed);

        await machine.Round().TakeAsync(1, Cancel);

        Assert.Empty(machine.Events.Signalled);
    }

    private static EncodeJob Job(
        RecordingId recording,
        EncodeProfileId profile,
        EncodeDestination destination,
        EncodeJobStatus status)
        => EncodeJob.Rehydrate(
            EncodeJobId.New(),
            recording,
            profile,
            destination.Id,
            destination.OutputRoot,
            status,
            EncodeJob.FirstAttempt,
            Began,
            status is EncodeJobStatus.Queued ? null : Began.AddMinutes(1),
            status is EncodeJobStatus.Queued or EncodeJobStatus.Running ? null : Began.AddMinutes(2),
            status is EncodeJobStatus.Failed
                ? new EncodeFailureDetail(EncodeFailure.SourceMissing, "the file was not where the ledger said", Began.AddMinutes(2))
                : null,
            status is EncodeJobStatus.Completed ? EncodeFileName.Artefact(recording, profile) : null,
            null,
            null,
            null,
            null);

    private sealed class Machine
    {
        public Machine()
        {
            Profile = EncodeProfile.Define(
                EncodeProfileId.New(),
                new EncodeLabel("Standard"),
                EncodeCodec.H264,
                EncodeResolution.AsSource,
                Deinterlace.Leave,
                new ConstantRateFactor(22),
                new ConstantQuantiser(24),
                Began);
            Destination = EncodeDestination.Define(
                new EncodeDestinationId(Guid.NewGuid()),
                new EncodeLabel("Shelf"),
                new OutputRoot("encodes"),
                Profile.Id,
                Began);
            Profiles.Profiles.Add(Profile);
            Destinations.Destinations.Add(Destination);
        }

        public EncodeProfile Profile { get; }

        public EncodeDestination Destination { get; }

        public HeldRecordings Recordings { get; } = new();

        public HeldEncodeJobs Jobs { get; } = new();

        public HeldEncodeDestinations Destinations { get; } = new();

        public HeldEncodeProfiles Profiles { get; } = new();

        public SilentEvents Events { get; } = new();

        public EncodeIntakeRound Round()
            => new(
                Recordings,
                Jobs,
                Destinations,
                Profiles,
                Events,
                new HandTurnedClock(new DateTimeOffset(Now)),
                NullLogger<EncodeIntakeRound>.Instance);

        public Recording Recorded(RecordingOutcome outcome, DateTime? began = null)
        {
            Recording recording = Written(began ?? Began);
            recording.Abort((began ?? Began).AddMinutes(30));

            if (outcome is not RecordingOutcome.Complete)
            {
                recording.Note(new OutcomeDetail(
                    RecordingFault.ShortOfTheWindow,
                    null,
                    "the supply stopped before the window did",
                    (began ?? Began).AddMinutes(30)));
            }

            recording.Settle(
                outcome,
                outcome is RecordingOutcome.Failed ? 0 : 1_200_000,
                (began ?? Began).AddMinutes(30));

            return recording;
        }

        public Recording StillWriting() => Written(Began);

        private Recording Written(DateTime began)
        {
            var id = RecordingId.New();
            Recording recording = Recording.Begin(
                id,
                null,
                new ProgrammeRef(new NetworkId(32741), new ServiceId(1064), new EventId(8981), began),
                new OutputRoot("primary"),
                RecordingFileName.For(id, ".ts"),
                began,
                began.AddMinutes(30),
                new ProgrammeSnapshot("A programme", string.Empty, string.Empty, [], began),
                null,
                BroadcastGroupRole.Standalone,
                began,
                new TunerDeviceId("synthetic-0"));
            recording.Wrote(TimeSpan.FromMinutes(30));
            Recordings.Recordings.Add(recording);

            return recording;
        }
    }
}
