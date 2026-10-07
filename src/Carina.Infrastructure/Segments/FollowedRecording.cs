using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Infrastructure.Segments;

/// <summary>
/// A recording being followed for its learning data: which one, the file it is read from, the
/// programme in it, the version its data is made with, the state its record stands in while it is
/// read, and what the follow has been told since — that the recording has ended, or that it has gone.
/// </summary>
public sealed class FollowedRecording(RecordingId id, string source, ServiceId service, ExtractionVersion version)
{
    private const int StillRecording = 0;

    private const int HasEndedMark = 1;

    private const int HasGoneMark = 2;

    private int told;

    public RecordingId Id { get; } = id ?? throw new ArgumentNullException(nameof(id));

    public string Source { get; } = string.IsNullOrEmpty(source) ? throw new ArgumentException("A followed recording is read from a file.", nameof(source)) : source;

    public ServiceId Service { get; } = service ?? throw new ArgumentNullException(nameof(service));

    public ExtractionVersion Version { get; } = version ?? throw new ArgumentNullException(nameof(version));

    public LearningExtractionState Held { get; private init; } = LearningExtractionState.Following;

    public bool HasEnded => Volatile.Read(ref told) is HasEndedMark;

    public bool HasGone => Volatile.Read(ref told) is HasGoneMark;

    /// <summary>
    /// A recording that has already ended, read from the head of its file to the end without waiting
    /// there, its record reading.
    /// </summary>
    public static FollowedRecording Recorded(RecordingId id, string source, ServiceId service, ExtractionVersion version)
    {
        FollowedRecording recorded = new(id, source, service, version) { Held = LearningExtractionState.Reading };

        recorded.Ended();

        return recorded;
    }

    public void Ended() => Interlocked.CompareExchange(ref told, HasEndedMark, StillRecording);

    public void Went() => Volatile.Write(ref told, HasGoneMark);
}

/// <summary>
/// Follows one recording for its learning data.
/// </summary>
public interface ILearningFollower
{
    /// <summary>
    /// Reads the recording from its head until it has ended and its file is read to the end, it has
    /// gone, or its file can no longer be read, keeping the data as it is made, and settles the record
    /// of taking it out. A follow the caller stops leaves the record where it stood.
    /// </summary>
    Task FollowAsync(FollowedRecording recording, CancellationToken cancellationToken);
}
