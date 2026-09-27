using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Events;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Encodings;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class EncodeIntakeJobTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Began = new(2026, 9, 14, 3, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan BeforeFirstLook = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan BetweenLooks = TimeSpan.FromSeconds(30);

    [Fact(DisplayName = "the loop looks at nothing while the auto-run is off, and queues at the look after it is turned back on")]
    public async Task TheLoopQueuesNothingWhileTheAutoRunIsOffAndQueuesOnceItIsBackOn()
    {
        var machine = new Loop();
        machine.Recorded();
        machine.AutoRun.Standing = new EncodeAutoRunStanding(false, 2, true, Began);

        using EncodeIntakeJob job = machine.Job();
        await job.StartAsync(Cancel);
        await machine.WaitingAgain("the loop never settled into its first wait");

        machine.Clock.Turn(BeforeFirstLook);
        await machine.WaitingAgain("the loop never came back from the look with the auto-run off");

        Assert.Empty(machine.Jobs.Jobs);
        Assert.Empty(machine.Events.Signalled);

        machine.AutoRun.Standing = machine.AutoRun.Standing with { Automatically = true };
        machine.Clock.Turn(BetweenLooks);
        await Eventually.Happens(() => machine.Jobs.Jobs.Count is 1, "the recording was never queued once the auto-run was back on");

        await job.StopAsync(Cancel);

        Assert.Contains(AppEventName.EncodeJobs, machine.Events.Signalled);
    }

    [Fact(DisplayName = "a look that queued a recording does not queue it again at the next one")]
    public async Task ALookThatQueuedARecordingDoesNotQueueItAgain()
    {
        var machine = new Loop();
        machine.Recorded();

        using EncodeIntakeJob job = machine.Job();
        await job.StartAsync(Cancel);
        await machine.WaitingAgain("the loop never settled into its first wait");

        machine.Clock.Turn(BeforeFirstLook);
        await Eventually.Happens(() => machine.Jobs.Jobs.Count is 1, "the recording was never queued");
        await machine.WaitingAgain("the loop never came back from the look that queued");

        machine.Clock.Turn(BetweenLooks);
        await machine.WaitingAgain("the loop never came back from the second look");

        await job.StopAsync(Cancel);

        Assert.Single(machine.Jobs.Jobs);
    }

    [Fact(DisplayName = "a recording that started before the others and ended after them is queued, however far the loop has read")]
    public async Task ARecordingThatStartedBeforeTheOthersAndEndedAfterThemIsQueued()
    {
        var machine = new Loop();
        Recording longer = machine.StillWriting(Began);

        for (int made = 1; made <= EncodeIntakeRound.PerLook + 1; made++)
        {
            machine.Recorded(Began.AddMinutes(made));
        }

        using EncodeIntakeJob job = machine.Job();
        await job.StartAsync(Cancel);
        await machine.WaitingAgain("the loop never settled into its first wait");

        machine.Clock.Turn(BeforeFirstLook);
        await Eventually.Happens(() => machine.Jobs.Jobs.Count is EncodeIntakeRound.PerLook, "the first hundred were never queued");
        await machine.WaitingAgain("the loop never came back from the first look");
        machine.Clock.Turn(BetweenLooks);
        await Eventually.Happens(() => machine.Jobs.Jobs.Count is EncodeIntakeRound.PerLook + 1, "the last one was never queued");
        await machine.WaitingAgain("the loop never came back from the second look");

        machine.Ended(longer);
        machine.Clock.Turn(BetweenLooks);
        await machine.WaitingAgain("the loop never came back from the look after the longer recording ended");
        machine.Clock.Turn(BetweenLooks);
        await machine.WaitingAgain("the loop never came back from the look after that");

        await job.StopAsync(Cancel);

        Assert.Contains(machine.Jobs.Jobs, queued => queued.RecordingId.Equals(longer.Id));
    }

    private sealed class Loop
    {
        public Loop()
        {
            EncodeProfile profile = EncodeProfile.Define(
                EncodeProfileId.New(),
                new EncodeLabel("Standard"),
                EncodeCodec.H264,
                EncodeResolution.AsSource,
                Deinterlace.Leave,
                new ConstantRateFactor(22),
                new ConstantQuantiser(24),
                Began);
            Profiles.Profiles.Add(profile);
            Destinations.Destinations.Add(EncodeDestination.Define(
                new EncodeDestinationId(Guid.NewGuid()),
                new EncodeLabel("Shelf"),
                new OutputRoot("encodes"),
                profile.Id,
                Began));
        }

        public HandTurnedClock Clock { get; } = new(new DateTimeOffset(Began));

        public HeldRecordings Recordings { get; } = new();

        public HeldEncodeJobs Jobs { get; } = new();

        public HeldEncodeDestinations Destinations { get; } = new();

        public HeldEncodeProfiles Profiles { get; } = new();

        public SilentEvents Events { get; } = new();

        public StandingEncodeAutoRun AutoRun { get; } = new();

        public EncodeIntakeJob Job()
        {
            var services = new ServiceCollection();
            services.AddScoped<IEncodeIntakeReader>(_ => new HeldEncodeIntake(Recordings, Jobs));
            services.AddScoped<IEncodeJobRepository>(_ => Jobs);
            services.AddScoped<IEncodeDestinationRepository>(_ => Destinations);
            services.AddScoped<IEncodeProfileRepository>(_ => Profiles);
            services.AddScoped<IEncodeAutoRunReader>(_ => AutoRun);
            services.AddScoped<IAppEventPublisher>(_ => Events);
            services.AddScoped<TimeProvider>(_ => Clock);
            services.AddLogging();
            services.AddScoped<EncodeIntakeRound>();

            return new EncodeIntakeJob(
                services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                new EncodeSettings { BeforeFirstLook = BeforeFirstLook, BetweenLooks = BetweenLooks },
                Clock,
                NullLogger<EncodeIntakeJob>.Instance);
        }

        public Task WaitingAgain(string what) => Eventually.Happens(() => Clock.Pending is 1, what);

        public Recording Recorded(DateTime? began = null) => Ended(StillWriting(began ?? Began));

        public Recording StillWriting(DateTime began)
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
                new ProgrammeSnapshot(
                    "A programme",
                    string.Empty,
                    string.Empty,
                    [],
                    began,
                    AudioMode.Undetermined,
                    ProgrammeSnapshot.SoundsUnannounced),
                null,
                BroadcastGroupRole.Standalone,
                began,
                new TunerDeviceId("synthetic-0"));
            Recordings.Recordings.Add(recording);

            return recording;
        }

        public Recording Ended(Recording recording)
        {
            recording.Wrote(TimeSpan.FromMinutes(30));
            recording.Abort(recording.StartedAtActual.AddMinutes(30));
            recording.Settle(RecordingOutcome.Complete, 1_200_000, recording.StartedAtActual.AddMinutes(30));

            return recording;
        }
    }
}
