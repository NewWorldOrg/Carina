using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Encodings;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class EncodeIntakeReaderTests(RepositoryDatabase database) : IAsyncLifetime
{
    private const int All = 10_000;

    private static readonly DateTime Began = new(2026, 9, 6, 3, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact(DisplayName = "the recordings that ended with a file, asked to be encoded and hold no job are the ones answered, oldest start first")]
    public async Task TheRecordingsThatEndedWithAFileAskedToBeEncodedAndHoldNoJobAreAnsweredOldestStartFirst()
    {
        Recording later = await EndedAsync(Began.AddMinutes(10), RecordingOutcome.Complete);
        Recording earlier = await EndedAsync(Began, RecordingOutcome.Truncated);
        Recording failed = await EndedAsync(Began.AddMinutes(1), RecordingOutcome.Failed);
        Recording stillWriting = await BegunAsync(Began.AddMinutes(2));
        Recording unasked = await EndedAsync(Began.AddMinutes(3), RecordingOutcome.Complete, encode: false);
        Recording queued = await EndedAsync(Began.AddMinutes(4), RecordingOutcome.Complete);
        await QueuedAsync(queued.Id);
        Recording halfGone = await EndedAsync(Began.AddMinutes(5), RecordingOutcome.Complete);
        await LeftBehindAsync(halfGone.Id);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<RecordingId> waiting = await Intake(reading).NeverQueuedAsync(All, Cancel);

        RecordingId[] ours = [.. waiting.Where(id => new[] { later, earlier, failed, stillWriting, unasked, queued, halfGone }.Any(recording => recording.Id.Equals(id)))];
        Assert.Equal([earlier.Id, later.Id], ours);
    }

    [Fact]
    public async Task NoMoreAreAnsweredThanWereAskedFor()
    {
        await EndedAsync(Began.AddHours(1), RecordingOutcome.Complete);
        await EndedAsync(Began.AddHours(2), RecordingOutcome.Complete);

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        Assert.Single(await Intake(reading).NeverQueuedAsync(1, Cancel));
    }

    private async Task QueuedAsync(RecordingId recording)
    {
        EncodeProfile profile = EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel("Viewing"),
            EncodeCodec.H264,
            EncodeResolution.Hd,
            Deinterlace.EveryFrame,
            new ConstantRateFactor(22),
            new ConstantQuantiser(25),
            Began);
        EncodeDestination destination = EncodeDestination.Define(
            EncodeDestinationId.New(),
            new EncodeLabel("Shelf"),
            new OutputRoot("encodes"),
            profile.Id,
            Began);

        await using AsyncServiceScope writing = container.CreateAsyncScope();
        await Profiles(writing).AddAsync(profile, Cancel);
        await Destinations(writing).AddAsync(destination, Cancel);
        await Jobs(writing).AddAsync(
            EncodeJob.Queue(EncodeJobId.New(), recording, profile.Id, destination.Id, destination.OutputRoot, Began),
            Cancel);
    }

    private async Task LeftBehindAsync(RecordingId id)
    {
        await using AsyncServiceScope context = container.CreateAsyncScope();
        Recording loaded = await Context(context).FindAsync<Recording>([id], Cancel)
            ?? throw new InvalidOperationException("The recording that was just written is not there.");

        loaded.Erased(RecordingErasure.Refused(ErasureFault.FileLeftBehind, "the file could not be removed", 1), Began.AddHours(1));
        await Context(context).SaveChangesAsync(Cancel);
    }

    private async Task<Recording> EndedAsync(DateTime began, RecordingOutcome outcome, bool encode = true)
    {
        Recording recording = await BegunAsync(began, encode);

        await using AsyncServiceScope context = container.CreateAsyncScope();
        Recording loaded = await Context(context).FindAsync<Recording>([recording.Id], Cancel)
            ?? throw new InvalidOperationException("The recording that was just written is not there.");

        loaded.Abort(began.AddMinutes(30));

        if (outcome is not RecordingOutcome.Complete)
        {
            loaded.Note(new OutcomeDetail(RecordingFault.ShortOfTheWindow, null, "the supply stopped before the window did", began.AddMinutes(30)));
        }

        loaded.Settle(outcome, outcome is RecordingOutcome.Failed ? 0 : 1_200_000, began.AddMinutes(30));
        await Context(context).SaveChangesAsync(Cancel);

        return loaded;
    }

    private async Task<Recording> BegunAsync(DateTime began, bool encode = true)
    {
        RecordingId id = RecordingId.New();
        Recording recording = Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(32736), new ServiceId(1024), new EventId(7101), began),
            new OutputRoot("primary"),
            RecordingFileName.For(id, ".m2ts"),
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
            null,
            encode);

        await using AsyncServiceScope context = container.CreateAsyncScope();
        Context(context).Add(recording);
        await Context(context).SaveChangesAsync(Cancel);

        return recording;
    }

    private static IEncodeDestinationRepository Destinations(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IEncodeDestinationRepository>();

    private static IEncodeIntakeReader Intake(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IEncodeIntakeReader>();

    private static IEncodeJobRepository Jobs(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IEncodeJobRepository>();

    private static IEncodeProfileRepository Profiles(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IEncodeProfileRepository>();

    private static CarinaDbContext Context(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<CarinaDbContext>();
}
