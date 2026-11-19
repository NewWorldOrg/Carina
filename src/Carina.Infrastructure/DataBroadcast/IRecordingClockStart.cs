namespace Carina.Infrastructure.DataBroadcast;

public interface IRecordingClockStart
{
    Task<RecordingClockStartReading> ReadAsync(string source, CancellationToken cancellationToken);
}

public sealed record RecordingClockStartReading(TimeSpan? Start, string Note);
