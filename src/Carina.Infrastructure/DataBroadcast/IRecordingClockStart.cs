namespace Carina.Infrastructure.DataBroadcast;

public interface IRecordingClockStart
{
    /// <summary>
    /// Where a recorded file's own clock begins, the moment the captions taken from it are told from.
    /// </summary>
    Task<RecordingClockStartReading> ReadAsync(string source, CancellationToken cancellationToken);
}

/// <summary>
/// Where a recorded file's own clock begins, or nothing and what was said instead.
/// </summary>
public sealed record RecordingClockStartReading(TimeSpan? Start, string Note);
