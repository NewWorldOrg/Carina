using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Segments;

/// <summary>
/// What the learning data keeps of the programme a recording is of.
/// </summary>
public sealed record ProgrammeCopy
{
    public ProgrammeCopy(
        NetworkId networkId,
        ServiceId serviceId,
        DateTime programmeStartsAt,
        DateTime? programmeEndsAt,
        DateTime recordingStartedAt,
        string name,
        IReadOnlyList<ProgrammeGenre> genres,
        IReadOnlyList<ProgrammeMark> marks,
        int? episode,
        AudioMode audio,
        string? seriesName)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(serviceId);
        ArgumentNullException.ThrowIfNull(genres);
        ArgumentNullException.ThrowIfNull(marks);

        DateTime starts = UtcTimes.Required(programmeStartsAt, nameof(programmeStartsAt));
        DateTime? ends = UtcTimes.Optional(programmeEndsAt, nameof(programmeEndsAt));

        if (ends <= starts)
        {
            throw new ArgumentException("A programme ends after it starts.", nameof(programmeEndsAt));
        }

        if (episode is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(episode), episode, "An episode is counted from zero.");
        }

        if (!Enum.IsDefined(audio))
        {
            throw new ArgumentOutOfRangeException(nameof(audio), audio, "The sound of a programme is one of the modes kept.");
        }

        NetworkId = networkId;
        ServiceId = serviceId;
        ProgrammeStartsAt = starts;
        ProgrammeEndsAt = ends;
        RecordingStartedAt = UtcTimes.Required(recordingStartedAt, nameof(recordingStartedAt));
        Name = Named(name, nameof(name));
        Genres = [.. genres];
        Marks = [.. marks];
        Episode = episode;
        Audio = audio;
        SeriesName = seriesName is null ? null : Named(seriesName, nameof(seriesName));
    }

    public NetworkId NetworkId { get; }

    public ServiceId ServiceId { get; }

    public DateTime ProgrammeStartsAt { get; }

    public DateTime? ProgrammeEndsAt { get; }

    public DateTime RecordingStartedAt { get; }

    public string Name { get; }

    public IReadOnlyList<ProgrammeGenre> Genres { get; }

    public IReadOnlyList<ProgrammeMark> Marks { get; }

    public int? Episode { get; }

    public AudioMode Audio { get; }

    public string? SeriesName { get; }

    public static ProgrammeCopy Of(Recording recording, DateTime? programmeEndsAt)
    {
        ArgumentNullException.ThrowIfNull(recording);

        return new ProgrammeCopy(
            recording.NetworkId,
            recording.ServiceId,
            recording.ProgrammeStartsAt,
            programmeEndsAt,
            recording.StartedAtActual,
            recording.SnapshotName,
            recording.SnapshotGenres,
            ProgrammeMarks.In(recording.SnapshotName, recording.SnapshotSummary),
            null,
            recording.SnapshotAudio,
            null);
    }

    public bool Equals(ProgrammeCopy? other)
        => other is not null
           && NetworkId.Equals(other.NetworkId)
           && ServiceId.Equals(other.ServiceId)
           && ProgrammeStartsAt == other.ProgrammeStartsAt
           && ProgrammeEndsAt == other.ProgrammeEndsAt
           && RecordingStartedAt == other.RecordingStartedAt
           && string.Equals(Name, other.Name, StringComparison.Ordinal)
           && Genres.SequenceEqual(other.Genres)
           && Marks.SequenceEqual(other.Marks)
           && Episode == other.Episode
           && Audio == other.Audio
           && string.Equals(SeriesName, other.SeriesName, StringComparison.Ordinal);

    public override int GetHashCode()
        => HashCode.Combine(NetworkId, ServiceId, ProgrammeStartsAt, RecordingStartedAt, Name);

    private static string Named(string name, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(name, parameterName);

        if (name.Length > Reservation.NameMaxLength)
        {
            throw new ArgumentException($"A name is kept in at most {Reservation.NameMaxLength} characters.", parameterName);
        }

        return name;
    }
}
