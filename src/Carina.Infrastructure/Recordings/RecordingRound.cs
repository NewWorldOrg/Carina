using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Infrastructure.Recordings;

public enum RecordingRefusalKind
{
    TuningRefused = 1,

    ClaimLostToAnother = 2,

    TunerContended = 3,

    DriverRefused = 4,

    DriverUnreachable = 5,
}

public sealed record RecordingRefusal(
    ReservationId Reservation,
    RecordingRefusalKind Kind,
    TuningRefusal Refusal,
    string Note);

public sealed record RecordingRun(
    IReadOnlyList<RecordingId> Started,
    IReadOnlyList<RecordingId> Stopped,
    IReadOnlyList<RecordingRefusal> Refused);

public sealed class RecordingRound(
    IReservationRecordingContract reservations,
    IRecordingRepository recordings,
    IServiceTuningDirectory directory,
    DiskPrecheckService disks,
    IDriverClient driver,
    RecordingSettings settings,
    TimeProvider clock)
{
    public const string WindowClosed = "the window this recording was promised has closed";

    private const string NothingSaidWhy = "the driver said nothing about why";

    private static readonly TunerKind HeaviestKind =
        ExpectedBitrate.Terrestrial.MostBitsPerSecond >= ExpectedBitrate.Satellite.MostBitsPerSecond
            ? TunerKind.Terrestrial
            : TunerKind.Satellite;

    public async Task<RecordingRun> RunAsync(CancellationToken cancellationToken)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        List<Recording> running = [.. await recordings.ListInFlightAsync(cancellationToken)];

        IReadOnlyList<RecordingId> stopped = await StopAsync(running, now, cancellationToken);
        (IReadOnlyList<RecordingId> started, IReadOnlyList<RecordingRefusal> refused) =
            await StartAsync(running, now, cancellationToken);

        return new RecordingRun(started, stopped, refused);
    }

    private async Task<IReadOnlyList<RecordingId>> StopAsync(
        List<Recording> running,
        DateTime now,
        CancellationToken cancellationToken)
    {
        List<RecordingId> stopped = [];

        foreach (Recording recording in running.ToList())
        {
            if (recording.ExpectedWindowEnd > now || recording.AbortedAt is not null)
            {
                continue;
            }

            DriverCall<SessionSnapshot> answer = await driver.StopSessionAsync(
                RecordingSessions.Named(recording.Id),
                WindowClosed,
                cancellationToken);

            if (answer.Outcome is not DriverCallOutcome.Reached)
            {
                continue;
            }

            recording.Abort(now);

            await recordings.SaveAsync(recording, cancellationToken);

            running.Remove(recording);
            stopped.Add(recording.Id);
        }

        return stopped;
    }

    private async Task<(IReadOnlyList<RecordingId> Started, IReadOnlyList<RecordingRefusal> Refused)> StartAsync(
        List<Recording> running,
        DateTime now,
        CancellationToken cancellationToken)
    {
        List<RecordingId> started = [];
        List<RecordingRefusal> refused = [];

        foreach (RecordingTick due in await reservations.DueAtAsync(now, cancellationToken))
        {
            if (due.InFlight)
            {
                continue;
            }

            TuningResolution resolution = await directory.ResolveTuningAsync(
                due.NetworkId,
                due.ServiceId,
                cancellationToken);

            if (resolution.Tuning is not { } tuning)
            {
                refused.Add(new RecordingRefusal(
                    due.Id,
                    RecordingRefusalKind.TuningRefused,
                    resolution.Refusal,
                    resolution.Refusal.ToString()));

                continue;
            }

            if (!await reservations.ClaimAsync(due.Id, now, cancellationToken))
            {
                refused.Add(new RecordingRefusal(
                    due.Id,
                    RecordingRefusalKind.ClaimLostToAnother,
                    TuningRefusal.None,
                    "another recorder holds the claim on this reservation"));

                continue;
            }

            RecordingWindow window = RecordingWindow.Promised(
                due.EffectiveStartAt,
                due.EffectiveEndAt,
                settings.TuningLead);
            TuneParams tune = tuning.Typed();
            var id = RecordingId.New();

            DiskPrecheckVerdict verdict = await disks.WeighAsync(
                settings.OutputRoot,
                new RecordingDemand(tune.Kind, window.Start, window.End),
                [.. running.Select(AtTheHeaviestRate)],
                now,
                cancellationToken);

            DriverCall<SessionSnapshot> answer = await driver.StartSessionAsync(
                Request(id, tune, due.EffectiveEndAt),
                cancellationToken);

            if (!answer.TryGetValue(out SessionSnapshot? session))
            {
                await reservations.ReleaseAsync(due.Id, now, cancellationToken);
                refused.Add(Refusal(due.Id, answer));

                continue;
            }

            Recording recording = Recording.Begin(
                id,
                due.Id,
                due.Programme,
                settings.OutputRoot,
                RecordingFileName.For(id, RecordingSettings.FileExtension),
                window.Start,
                window.End,
                due.Snapshot,
                due.BroadcastGroupKey,
                due.BroadcastGroupRole,
                now,
                session.DeviceId is { Length: > 0 } named ? new TunerDeviceId(named) : null);

            if (!verdict.HasRoom)
            {
                recording.Note(verdict.Detail(now));
            }

            await recordings.AddAsync(recording, cancellationToken);

            running.Add(recording);
            started.Add(id);
        }

        return (started, refused);
    }

    private static RecordingRefusal Refusal(ReservationId reservation, DriverCall<SessionSnapshot> answer)
    {
        string title = answer.Problem?.Title ?? NothingSaidWhy;

        RecordingRefusalKind kind = answer.Outcome is DriverCallOutcome.Unreachable
            ? RecordingRefusalKind.DriverUnreachable
            : title is SessionRefusalTitles.NoDeviceFree or SessionRefusalTitles.DeviceBusy
                ? RecordingRefusalKind.TunerContended
                : RecordingRefusalKind.DriverRefused;

        return new RecordingRefusal(reservation, kind, TuningRefusal.None, title);
    }

    private static RecordingDemand AtTheHeaviestRate(Recording recording)
        => new(HeaviestKind, recording.ExpectedWindowStart, recording.ExpectedWindowEnd);

    private StartSessionRequest Request(RecordingId id, TuneParams tune, DateTime endsAt)
        => new()
        {
            SessionId = RecordingSessions.Named(id),
            Purpose = SessionPurpose.Recording,
            Tuning = tune.ToLegacyRequest(),
            Tune = tune,
            OutputRoot = settings.OutputRoot.Value,
            RecordingId = id.Wire,
            EndsAt = endsAt,
        };
}
