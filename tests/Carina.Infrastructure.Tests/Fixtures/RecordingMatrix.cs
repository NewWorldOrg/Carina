using Carina.Domain.Base;
using Carina.Domain.Captions;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Thumbnails;
using Carina.Infrastructure.Captions;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.Fixtures.Recordings;

/// <summary>
/// A matrix of recordings in every standing of their picture and their captions, with captions taken on the very
/// instant the file was descrambled and one microsecond either side of it, for the tests that hold a query of the store
/// to the recording's own answer for the same rows.
/// </summary>
internal static class RecordingMatrix
{
    public static readonly DateTime Began = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    public static readonly DateTime Ended = Began.AddHours(1);

    public static readonly DateTime Taken = Began.AddHours(2);

    public static readonly TimeSpan Microsecond = TimeSpan.FromTicks(10);

    public static readonly OutputRoot Bulk = new("bulk");

    public static readonly OutputRoot Spare = new("spare");

    /// <summary>
    /// Lays the matrix on an empty ledger and reads it back, so both arms judge the instants the store keeps.
    /// </summary>
    public static Task<IReadOnlyList<Recording>> LaidAsync(ServiceProvider container) => LaidAsync(container, Shapes());

    public static async Task<IReadOnlyList<Recording>> LaidAsync(ServiceProvider container, IEnumerable<Func<Recording, Recording>> shapes)
    {
        Func<Recording, Recording>[] laid = [.. shapes];

        await using (AsyncServiceScope clearing = container.CreateAsyncScope())
        {
            await clearing.ServiceProvider.GetRequiredService<CarinaDbContext>().Database.ExecuteSqlRawAsync("TRUNCATE recording CASCADE");
        }

        await using (AsyncServiceScope scope = container.CreateAsyncScope())
        {
            CarinaDbContext context = scope.ServiceProvider.GetRequiredService<CarinaDbContext>();
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

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        return await reading.ServiceProvider.GetRequiredService<CarinaDbContext>().Set<Recording>().AsNoTracking().ToListAsync();
    }

    public static IEnumerable<Func<Recording, Recording>> Shapes()
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

    /// <summary>
    /// Takes the captions, then descrambles the file <paramref name="apart"/> after: before the captions when it is
    /// negative, on the same instant when it is zero.
    /// </summary>
    public static Recording DescrambledAround(Recording recording, TimeSpan apart, CaptionState state)
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

    public static Recording FailedCaptions(Recording recording, int times)
    {
        for (int failed = 1; failed <= times; failed++)
        {
            recording.Caption(CaptionState.Failed, null, Taken.AddMinutes(failed));
        }

        return recording;
    }

    public static Recording Captioned(Recording recording, CaptionState state, int? pictures)
    {
        recording.Caption(state, pictures, Taken);

        return recording;
    }

    public static Recording Pictured(Recording recording, ThumbnailState state, ThumbnailFault? fault)
    {
        recording.Illustrate(state, fault);

        return recording;
    }

    public static Recording Settled(Recording recording, RecordingOutcome outcome, bool scrambled = false)
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

    public static Recording Started(OutputRoot root, int eventId)
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

    public static Guid[] Ids(IEnumerable<Recording> recordings) => Ids(recordings.Select(recording => recording.Id));

    public static Guid[] Ids(IEnumerable<RecordingId> ids) => [.. ids.Select(id => id.Value).Order()];
}
