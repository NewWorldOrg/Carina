namespace Carina.Infrastructure.Tests.Fixtures.Recordings;

internal sealed class RecordingJob
{
    public int Id { get; set; }
    public int GuideEntryId { get; set; }
}

internal sealed class TapeEntry
{
    public int Id { get; set; }
    public int ChannelLineupId { get; set; }
    public int BookingId { get; set; }
}
