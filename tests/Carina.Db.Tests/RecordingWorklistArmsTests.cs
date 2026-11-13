using Carina.Domain.Base;
using Carina.Domain.Captions;
using Carina.Domain.Channels;
using Carina.Domain.DataBroadcast;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Thumbnails;
using Carina.Infrastructure.Captions;
using Carina.Infrastructure.DataBroadcast;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.Infrastructure.Thumbnails;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Db.Tests;

/// <summary>
/// The queries the caption pass and the data broadcast pass run in the store, held to the recording's own answer for the
/// same rows. A matrix of recordings in every standing of their picture, their captions and the record of their data
/// broadcast goes through both, with captions taken on the very instant the file was descrambled and one microsecond
/// either side of it, and a record that failed on its last try and on the one before it.
/// </summary>
[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RecordingWorklistArmsTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private static readonly DateTime Began = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Ended = Began.AddHours(1);

    private static readonly DateTime Taken = Began.AddHours(2);

    private static readonly TimeSpan Microsecond = TimeSpan.FromTicks(10);

    private static readonly OutputRoot Bulk = new("bulk");

    private static readonly OutputRoot Spare = new("spare");

    [Fact(DisplayName = "BR-PD-016: the recordings the caption pass reads as waiting are the ones each recording says awaits its captions, on the instant of the descrambling and either side of it")]
    public async Task TheRecordingsWaitingForCaptionsAreTheOnesEachSaysAwaitsThem()
    {
        IReadOnlyList<Recording> matrix = await LaidAsync();

        Assert.Contains(matrix, recording => recording.AwaitsCaptions);
        Assert.Contains(matrix, recording => !recording.AwaitsCaptions);
        Assert.Contains(matrix, recording => recording.DescrambledAt is { } at && at == recording.CaptionsMadeAt);

        await using CarinaDbContext context = Context();
        var worklist = new CaptionWorklist(context, TimeProvider.System);

        IReadOnlyList<CaptionSubject> within = await worklist.AwaitingAsync([Bulk], 1_000, CancellationToken.None);
        int beyond = await worklist.WaitingOutOfReachAsync([Bulk], CancellationToken.None);

        Assert.Equal(
            Ids(matrix.Where(recording => recording.AwaitsCaptions && recording.OutputRoot.Equals(Bulk))),
            Ids(within.Select(subject => subject.Id)));
        Assert.Equal(matrix.Count(recording => recording.AwaitsCaptions && recording.OutputRoot.Equals(Spare)), beyond);
    }

    [Fact(DisplayName = "BR-BS-001: the recordings the data broadcast pass reads as waiting, puts to coming once ended and tries again are the ones each recording says so of, on the last try and the one before it")]
    public async Task TheRecordingsTheDataBroadcastPassMovesAreTheOnesEachSaysSoOf()
    {
        IReadOnlyList<Recording> laid = await LaidAsync(DataBroadcastShapes());

        await using (NpgsqlConnection connection = await database.OpenAsync())
        {
            Guid[] behind = [.. laid.Where(recording => recording.AwaitsItsDataBroadcast && recording.DataBroadcastAttempts == 0)
                .Where((_, index) => index % 3 == 0)
                .Select(recording => recording.Id.Value)];
            await using var forgetting = new NpgsqlCommand(
                "UPDATE recording SET data_broadcast_state = 'None' WHERE id = ANY(@ids)",
                connection);
            forgetting.Parameters.AddWithValue("ids", behind);
            await forgetting.ExecuteNonQueryAsync();
        }

        IReadOnlyList<Recording> matrix = await ReadAllAsync();

        Assert.Contains(matrix, recording => recording.AwaitsItsDataBroadcast);
        Assert.Contains(matrix, recording => recording.DataBroadcastNeverCameDue);
        Assert.Contains(matrix, recording => recording.DataBroadcastIsDueAgain);
        Assert.Contains(matrix, recording => recording.DataBroadcastAttempts == DataBroadcastProgress.TriesAtMost);
        Assert.Contains(matrix, recording => recording is { IsInFlight: true, DataBroadcastState: DataBroadcastState.None });

        await using (CarinaDbContext context = Context())
        {
            var worklist = new DataBroadcastWorklist(context, TimeProvider.System);

            IReadOnlyList<DataBroadcastSubject> within = await worklist.AwaitingAsync([Bulk], 1_000, CancellationToken.None);
            int beyond = await worklist.WaitingOutOfReachAsync([Bulk], CancellationToken.None);

            Assert.Equal(
                Ids(matrix.Where(recording => recording.AwaitsItsDataBroadcast && recording.OutputRoot.Equals(Bulk))),
                Ids(within.Select(subject => subject.Id)));
            Assert.Equal(matrix.Count(recording => recording.AwaitsItsDataBroadcast && recording.OutputRoot.Equals(Spare)), beyond);
            Assert.Equal(matrix.Count(recording => recording.DataBroadcastNeverCameDue), await worklist.CatchUpEndedAsync(CancellationToken.None));
            Assert.Equal(matrix.Count(recording => recording.DataBroadcastIsDueAgain), await worklist.RetryFailedAsync(CancellationToken.None));
        }

        Guid[] moved = Ids(matrix.Where(recording => recording.DataBroadcastNeverCameDue || recording.DataBroadcastIsDueAgain));
        IReadOnlyList<Recording> after = await ReadAllAsync();

        Assert.Equal(
            Ids(matrix.Where(recording => recording.AwaitsItsDataBroadcast).Select(recording => recording.Id)).Concat(moved).Order().ToArray(),
            Ids(after.Where(recording => recording.AwaitsItsDataBroadcast)));
    }

    /// <summary>
    /// Lays the matrix on an empty ledger and reads it back, so both arms judge the instants the store keeps.
    /// </summary>
    private Task<IReadOnlyList<Recording>> LaidAsync() => LaidAsync(Shapes());

    private async Task<IReadOnlyList<Recording>> LaidAsync(IEnumerable<Func<Recording, Recording>> shapes)
    {
        Func<Recording, Recording>[] laid = [.. shapes];

        await using (NpgsqlConnection connection = await database.OpenAsync())
        {
            await using var clearing = new NpgsqlCommand("TRUNCATE recording CASCADE", connection);
            await clearing.ExecuteNonQueryAsync();
        }

        await using (CarinaDbContext context = Context())
        {
            int eventId = 0;

            foreach (OutputRoot root in new[] { Bulk, Spare })
            {
                foreach (Func<Recording, Recording> shape in laid)
                {
                    context.Add(shape(Started(root, ++eventId)));
                }
            }

            await context.SaveChangesAsync();
        }

        await using CarinaDbContext reading = Context();

        return await reading.Set<Recording>().AsNoTracking().ToListAsync();
    }

    private async Task<IReadOnlyList<Recording>> ReadAllAsync()
    {
        await using CarinaDbContext reading = Context();

        return await reading.Set<Recording>().AsNoTracking().ToListAsync();
    }

    private static IEnumerable<Func<Recording, Recording>> Shapes()
    {
        yield return recording => recording;

        foreach (RecordingOutcome outcome in new[] { RecordingOutcome.Truncated, RecordingOutcome.Failed })
        {
            yield return recording => Settled(recording, outcome);
            yield return recording => Pictured(Settled(recording, outcome), ThumbnailState.Skipped, null);
            yield return recording => Pictured(Settled(recording, outcome), ThumbnailState.Failed, ThumbnailFault.TimedOut);
        }

        yield return recording => Pictured(Settled(recording, RecordingOutcome.Truncated), ThumbnailState.Ready, null);
        yield return recording => Captioned(Settled(recording, RecordingOutcome.Truncated), CaptionState.Ready, 3);
        yield return recording => Captioned(Settled(recording, RecordingOutcome.Truncated), CaptionState.Absent, null);

        for (int failed = 1; failed <= CaptionSettings.TriesAtMost; failed++)
        {
            int times = failed;

            yield return recording => FailedCaptions(Settled(recording, RecordingOutcome.Truncated), times);
        }

        foreach (TimeSpan apart in new[] { -Microsecond, TimeSpan.Zero, Microsecond })
        {
            yield return recording => DescrambledAround(recording, apart, CaptionState.Ready);
            yield return recording => DescrambledAround(recording, apart, CaptionState.Absent);
        }

        yield return recording => DescrambledAround(
            FailedCaptions(Settled(recording, RecordingOutcome.Truncated, scrambled: true), CaptionSettings.TriesAtMost - 1),
            Microsecond,
            CaptionState.Failed);
        yield return recording => Settled(recording, RecordingOutcome.Truncated, scrambled: true);
    }

    private static IEnumerable<Func<Recording, Recording>> DataBroadcastShapes()
    {
        yield return recording => recording;

        foreach (RecordingOutcome outcome in new[] { RecordingOutcome.Truncated, RecordingOutcome.Failed })
        {
            yield return recording => Settled(recording, outcome);
            yield return recording => Settled(recording, outcome);
            yield return recording => Settled(recording, outcome);
        }

        yield return recording => TakenData(Settled(recording, RecordingOutcome.Truncated), 0);
        yield return recording => TakenData(Settled(recording, RecordingOutcome.Truncated), 36);
        yield return recording => Again(TakenData(Settled(recording, RecordingOutcome.Truncated), 36));

        for (int failed = 1; failed <= DataBroadcastProgress.TriesAtMost; failed++)
        {
            int times = failed;

            yield return recording => FailedData(Settled(recording, RecordingOutcome.Truncated), times);
        }

        for (int failed = 1; failed < DataBroadcastProgress.TriesAtMost; failed++)
        {
            int times = failed;

            yield return recording => Again(FailedData(Settled(recording, RecordingOutcome.Truncated), times));
        }

        yield return recording =>
        {
            Recording taken = TakenData(Settled(recording, RecordingOutcome.Truncated, scrambled: true), 36);
            taken.Descrambled(Taken.AddHours(1));

            return taken;
        };
    }

    private static Recording TakenData(Recording recording, int modules)
    {
        recording.DataBroadcastTaken(modules, Taken);

        return recording;
    }

    private static Recording FailedData(Recording recording, int times)
    {
        for (int failed = 1; failed <= times; failed++)
        {
            if (failed > 1)
            {
                recording.DataBroadcastAgain();
            }

            recording.DataBroadcastFailed(Taken.AddMinutes(failed));
        }

        return recording;
    }

    private static Recording Again(Recording recording)
    {
        recording.DataBroadcastAgain();

        return recording;
    }

    /// <summary>
    /// Takes the captions, then descrambles the file <paramref name="apart"/> after: before the captions when it is
    /// negative, on the same instant when it is zero.
    /// </summary>
    private static Recording DescrambledAround(Recording recording, TimeSpan apart, CaptionState state)
    {
        Recording ended = recording.IsInFlight ? Settled(recording, RecordingOutcome.Truncated, scrambled: true) : recording;
        DateTime captioned = Taken.AddHours(1);

        if (apart < TimeSpan.Zero)
        {
            ended.Descrambled(captioned + apart);
            ended.Caption(state, state is CaptionState.Ready ? 3 : null, captioned);
        }
        else
        {
            ended.Caption(state, state is CaptionState.Ready ? 3 : null, captioned);
            ended.Descrambled(captioned + apart);
        }

        return ended;
    }

    private static Recording FailedCaptions(Recording recording, int times)
    {
        for (int failed = 1; failed <= times; failed++)
        {
            recording.Caption(CaptionState.Failed, null, Taken.AddMinutes(failed));
        }

        return recording;
    }

    private static Recording Captioned(Recording recording, CaptionState state, int? pictures)
    {
        recording.Caption(state, pictures, Taken);

        return recording;
    }

    private static Recording Pictured(Recording recording, ThumbnailState state, ThumbnailFault? fault)
    {
        recording.Illustrate(state, fault);

        return recording;
    }

    private static Recording Settled(Recording recording, RecordingOutcome outcome, bool scrambled = false)
    {
        recording.Wrote(TimeSpan.FromHours(1));
        recording.Note(new OutcomeDetail(
            scrambled ? RecordingFault.ScramblingUnresolved : RecordingFault.DriverLost,
            null,
            string.Empty,
            Began));
        recording.Abort(Ended);
        recording.Settle(outcome, outcome is RecordingOutcome.Failed ? 0 : 1_000, Ended);

        return recording;
    }

    private static Recording Started(OutputRoot root, int eventId)
    {
        RecordingId id = RecordingId.New();

        return Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(32736), new ServiceId(1024), new EventId(eventId), Began),
            root,
            RecordingFileName.For(id, ".m2ts"),
            Began,
            Ended,
            new ProgrammeSnapshot(
                "A programme",
                string.Empty,
                string.Empty,
                [],
                Began,
                AudioMode.Undetermined,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            Began,
            new TunerDeviceId("pt3-0"));
    }

    private static Guid[] Ids(IEnumerable<Recording> recordings) => Ids(recordings.Select(recording => recording.Id));

    private static Guid[] Ids(IEnumerable<RecordingId> ids) => [.. ids.Select(id => id.Value).Order()];

    private CarinaDbContext Context() => CarinaDbContextFactory.Create(database.ConnectionString);
}
