using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Collection;

namespace Carina.Infrastructure.Recordings;

public enum RecordingRefusalKind
{
    TuningRefused = 1,

    ClaimLostToAnother = 2,

    TunerContended = 3,

    DriverRefused = 4,

    DriverUnreachable = 5,

    StartAbandoned = 6,
}

public sealed record RecordingRefusal(
    ReservationId Reservation,
    RecordingRefusalKind Kind,
    TuningRefusal Refusal,
    string Note);

public sealed record RecordingRun(
    IReadOnlyList<RecordingId> Started,
    IReadOnlyList<RecordingId> Stopped,
    IReadOnlyList<RecordingId> Unconfirmed,
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

    public const string StartAbandoned = "the ledger would not take the row this session belongs to";

    private const string ClaimHeldElsewhere = "another recorder holds the claim on this reservation";

    private const string NothingSaidWhy = "the driver said nothing about why";

    private static readonly TunerKind HeaviestKind =
        ExpectedBitrate.Terrestrial.MostBitsPerSecond >= ExpectedBitrate.Satellite.MostBitsPerSecond
            ? TunerKind.Terrestrial
            : TunerKind.Satellite;

    private enum SessionStanding
    {
        Standing = 1,

        NothingStarted = 2,

        Unknowable = 3,
    }

    public async Task<RecordingRun> RunAsync(CancellationToken cancellationToken)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        List<Recording> running = [.. await recordings.ListInFlightAsync(cancellationToken)];

        IReadOnlyList<RecordingId> stopped = await StopAsync(running, now, cancellationToken);
        Starting starting = await StartAsync(running, now, cancellationToken);

        return new RecordingRun(starting.Started, stopped, starting.Unconfirmed, starting.Refused);
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

    private async Task<Starting> StartAsync(
        List<Recording> running,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var starting = new Starting();

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
                starting.Refused.Add(new RecordingRefusal(
                    due.Id,
                    RecordingRefusalKind.TuningRefused,
                    resolution.Refusal,
                    resolution.Refusal.ToString()));

                continue;
            }

            if (!await reservations.ClaimAsync(due.Id, now, cancellationToken))
            {
                starting.Refused.Add(new RecordingRefusal(
                    due.Id,
                    RecordingRefusalKind.ClaimLostToAnother,
                    TuningRefusal.None,
                    ClaimHeldElsewhere));

                continue;
            }

            await ClaimedAsync(due, tuning, running, starting, now, cancellationToken);
        }

        return starting;
    }

    private async Task ClaimedAsync(
        RecordingTick due,
        TuningParameters tuning,
        List<Recording> running,
        Starting starting,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var id = RecordingId.New();
        SessionId? issued = null;

        try
        {
            RecordingWindow window = RecordingWindow.Promised(
                due.EffectiveStartAt,
                due.EffectiveEndAt,
                settings.TuningLead);
            TuneParams tune = tuning.Typed();

            DiskPrecheckVerdict verdict = await disks.WeighAsync(
                settings.OutputRoot,
                new RecordingDemand(tune.Kind, window.Start, window.End),
                [.. running.Select(AtTheHeaviestRate)],
                now,
                cancellationToken);

            issued = RecordingSessions.Named(id);

            DriverCall<SessionSnapshot> answer = await driver.StartSessionAsync(
                Request(id, tune, due.EffectiveEndAt),
                cancellationToken);
            (SessionStanding standing, SessionSnapshot? session) = await StandingAsync(
                answer,
                issued.Value,
                cancellationToken);

            if (standing is SessionStanding.NothingStarted)
            {
                issued = null;

                await reservations.ReleaseAsync(due.Id, now, CancellationToken.None);

                starting.Refused.Add(Refusal(due.Id, answer));

                return;
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
                session?.DeviceId is { Length: > 0 } named ? new TunerDeviceId(named) : null);

            if (!verdict.HasRoom)
            {
                recording.Note(verdict.Detail(now));
            }

            await recordings.AddAsync(recording, cancellationToken);

            running.Add(recording);
            starting.Started.Add(id);

            if (standing is SessionStanding.Unknowable)
            {
                starting.Unconfirmed.Add(id);
            }
        }
        catch (Exception failure)
        {
            if (!await AbandonAsync(due.Id, issued, now))
            {
                starting.Unconfirmed.Add(id);
            }

            if (failure is OperationCanceledException)
            {
                throw;
            }

            starting.Refused.Add(new RecordingRefusal(
                due.Id,
                RecordingRefusalKind.StartAbandoned,
                TuningRefusal.None,
                failure.GetType().Name));
        }
    }

    private async Task<(SessionStanding Standing, SessionSnapshot? Session)> StandingAsync(
        DriverCall<SessionSnapshot> answer,
        SessionId issued,
        CancellationToken cancellationToken)
    {
        if (answer.TryGetValue(out SessionSnapshot? session))
        {
            return (SessionStanding.Standing, session);
        }

        if (answer.Outcome is DriverCallOutcome.Refused)
        {
            return (SessionStanding.NothingStarted, null);
        }

        DriverCall<SessionSnapshot> asked = await driver.GetSessionAsync(issued, cancellationToken);

        if (asked.TryGetValue(out SessionSnapshot? held))
        {
            return (SessionStanding.Standing, held);
        }

        return asked.Outcome is DriverCallOutcome.Refused
            ? (SessionStanding.NothingStarted, null)
            : (SessionStanding.Unknowable, null);
    }

    private async Task<bool> AbandonAsync(ReservationId reservation, SessionId? issued, DateTime claimedAt)
    {
        try
        {
            if (issued is { } sessionId)
            {
                DriverCall<SessionSnapshot> stopped = await driver.StopSessionAsync(
                    sessionId,
                    StartAbandoned,
                    CancellationToken.None);

                if (stopped.Outcome is not DriverCallOutcome.Reached)
                {
                    return false;
                }
            }

            await reservations.ReleaseAsync(reservation, claimedAt, CancellationToken.None);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static RecordingRefusal Refusal(ReservationId reservation, DriverCall<SessionSnapshot> answer)
    {
        string title = answer.Problem?.Title ?? NothingSaidWhy;

        RecordingRefusalKind kind = answer.Outcome is DriverCallOutcome.Unreachable
            ? RecordingRefusalKind.DriverUnreachable
            : SessionRefusalReading.IsContended(answer.Problem)
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

    private sealed class Starting
    {
        public List<RecordingId> Started { get; } = [];

        public List<RecordingId> Unconfirmed { get; } = [];

        public List<RecordingRefusal> Refused { get; } = [];
    }
}
