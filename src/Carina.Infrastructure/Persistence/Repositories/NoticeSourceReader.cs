using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Notifications;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

/// <summary>
/// Reads the recording, reservation outcome, encode and quality ledgers for the rows a notice may be
/// taken from, with the names their words need.
/// </summary>
public sealed class NoticeSourceReader(CarinaDbContext context) : INoticeSourceReader
{
    public async Task<IReadOnlyList<NoticeSource>> SinceAsync(DateTime since, CancellationToken cancellationToken)
    {
        List<HeldRecording> recordings = await RecordingsAsync(since, cancellationToken);
        List<ReservationOutcome> outcomes = await OutcomesAsync(since, cancellationToken);
        Dictionary<(NetworkId Network, ServiceId Service), string> stations = recordings.Count + outcomes.Count is 0
            ? []
            : await StationsAsync(cancellationToken);
        Dictionary<Guid, string> reserved = await ReservedNamesAsync(outcomes, cancellationToken);

        return
        [
            .. recordings.Select(recording => Source(recording, stations)),
            .. outcomes.Select(outcome => Source(outcome, stations, reserved)),
            .. await EncodesAsync(since, cancellationToken),
            .. await IncidentsAsync(since, cancellationToken),
        ];
    }

    private async Task<List<HeldRecording>> RecordingsAsync(DateTime since, CancellationToken cancellationToken)
        => await context.Set<Recording>()
            .AsNoTracking()
            .Where(recording => recording.StartedAtActual >= since || recording.StoppedAtActual >= since)
            .Select(recording => new HeldRecording(
                recording.Id,
                recording.ReservationId,
                recording.NetworkId,
                recording.ServiceId,
                recording.SnapshotName,
                recording.ProgrammeStartsAt,
                recording.ExpectedWindowEnd,
                recording.StartedAtActual,
                recording.StoppedAtActual,
                recording.Outcome,
                recording.MissedMs,
                recording.OutcomeDetail))
            .ToListAsync(cancellationToken);

    private async Task<List<ReservationOutcome>> OutcomesAsync(DateTime since, CancellationToken cancellationToken)
        => await context.Set<ReservationOutcome>()
            .AsNoTracking()
            .Where(outcome => outcome.OccurredAt >= since)
            .ToListAsync(cancellationToken);

    private async Task<Dictionary<(NetworkId Network, ServiceId Service), string>> StationsAsync(
        CancellationToken cancellationToken)
    {
        List<HeldStation> held = await context.Set<BroadcastService>()
            .AsNoTracking()
            .Select(service => new HeldStation(service.NetworkId, service.ServiceId, service.Name))
            .ToListAsync(cancellationToken);

        return held.ToDictionary(station => (station.Network, station.Service), station => station.Name);
    }

    private async Task<Dictionary<Guid, string>> ReservedNamesAsync(
        IReadOnlyList<ReservationOutcome> outcomes,
        CancellationToken cancellationToken)
    {
        ReservationId[] asked =
        [
            .. outcomes.SelectMany(outcome => outcome.RecordedInstead).Distinct().Select(id => new ReservationId(id)),
        ];

        if (asked.Length is 0)
        {
            return [];
        }

        List<HeldReservation> held = await context.Set<Reservation>()
            .AsNoTracking()
            .Where(reservation => asked.Contains(reservation.Id))
            .Select(reservation => new HeldReservation(reservation.Id, reservation.SnapshotName))
            .ToListAsync(cancellationToken);

        return held.ToDictionary(reservation => reservation.Id.Value, reservation => reservation.Name);
    }

    private async Task<List<NoticeSource>> EncodesAsync(DateTime since, CancellationToken cancellationToken)
    {
        List<EncodeJob> jobs = await context.Set<EncodeJob>()
            .AsNoTracking()
            .Where(job => job.StartedAt >= since || job.EndedAt >= since)
            .ToListAsync(cancellationToken);

        if (jobs.Count is 0)
        {
            return [];
        }

        RecordingId[] recorded = [.. jobs.Select(job => job.RecordingId).Distinct()];
        EncodeProfileId[] shaped = [.. jobs.Select(job => job.ProfileId).Distinct()];

        List<HeldName> names = await context.Set<Recording>()
            .AsNoTracking()
            .Where(recording => recorded.Contains(recording.Id))
            .Select(recording => new HeldName(recording.Id, recording.SnapshotName))
            .ToListAsync(cancellationToken);

        List<HeldCodec> codecs = await context.Set<EncodeProfile>()
            .AsNoTracking()
            .Where(profile => shaped.Contains(profile.Id))
            .Select(profile => new HeldCodec(profile.Id, profile.Codec))
            .ToListAsync(cancellationToken);

        Dictionary<RecordingId, string> nameOf = names.ToDictionary(name => name.Recording, name => name.Name);
        Dictionary<EncodeProfileId, EncodeCodec> codecOf = codecs.ToDictionary(codec => codec.Profile, codec => codec.Codec);

        return
        [
            .. jobs.Select(job => new EncodeNoticeSource(
                job.Id.Value,
                job.RecordingId.Value,
                nameOf.GetValueOrDefault(job.RecordingId, string.Empty),
                codecOf.TryGetValue(job.ProfileId, out EncodeCodec codec) ? codec : null,
                job.Status,
                job.StartedAt,
                job.EndedAt,
                job.Failure?.Failure)),
        ];
    }

    private async Task<List<NoticeSource>> IncidentsAsync(DateTime since, CancellationToken cancellationToken)
    {
        List<QualityIncident> incidents = await context.Set<QualityIncident>()
            .AsNoTracking()
            .Where(incident => incident.DetectedAt >= since || incident.ResolvedAt >= since)
            .ToListAsync(cancellationToken);

        return
        [
            .. incidents.Select(incident => new QualityNoticeSource(
                incident.Id.Value,
                incident.Owner,
                incident.Breached,
                incident.Subject.Kind,
                incident.Observed,
                incident.Applied.Current,
                TunerTroubles.Named(incident.Classification),
                incident.DetectedAt,
                incident.ResolvedAt)),
        ];
    }

    private static RecordingNoticeSource Source(
        HeldRecording recording,
        Dictionary<(NetworkId Network, ServiceId Service), string> stations)
        => new(
            recording.Id.Value,
            recording.Reservation is not null,
            recording.Name,
            stations.GetValueOrDefault((recording.Network, recording.Service)),
            recording.ProgrammeStartsAt,
            recording.EndsAt,
            recording.StartedAt,
            recording.SettledAt,
            recording.Outcome,
            recording.MissedMs,
            [.. recording.Detail.Select(detail => detail.Fault).Distinct()]);

    private static ReservationOutcomeNoticeSource Source(
        ReservationOutcome outcome,
        Dictionary<(NetworkId Network, ServiceId Service), string> stations,
        Dictionary<Guid, string> reserved)
        => new(
            outcome.Id.Value,
            outcome.Kind,
            outcome.SnapshotName,
            stations.GetValueOrDefault((outcome.NetworkId, outcome.ServiceId)),
            outcome.EffectiveStartAt,
            outcome.EffectiveEndAt,
            outcome.OccurredAt,
            outcome.Faults,
            outcome.GaveUpBecause,
            [.. outcome.RecordedInstead.Where(reserved.ContainsKey).Select(id => reserved[id])]);

    private sealed record HeldRecording(
        RecordingId Id,
        ReservationId? Reservation,
        NetworkId Network,
        ServiceId Service,
        string Name,
        DateTime ProgrammeStartsAt,
        DateTime EndsAt,
        DateTime StartedAt,
        DateTime? SettledAt,
        RecordingOutcome? Outcome,
        long MissedMs,
        IReadOnlyList<OutcomeDetail> Detail);

    private sealed record HeldStation(NetworkId Network, ServiceId Service, string Name);

    private sealed record HeldReservation(ReservationId Id, string Name);

    private sealed record HeldName(RecordingId Recording, string Name);

    private sealed record HeldCodec(EncodeProfileId Profile, EncodeCodec Codec);
}
